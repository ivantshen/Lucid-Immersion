"""
Integration tests for the /ask endpoint
"""
import pytest
import json
import os
import tempfile
import io
from unittest.mock import Mock, patch, MagicMock
from pathlib import Path
from werkzeug.datastructures import FileStorage
import sys
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Mock google.cloud.speech before importing app modules
sys.modules['google.cloud'] = MagicMock()
sys.modules['google.cloud.speech'] = MagicMock()

from app.main import create_app


@pytest.fixture
def app():
    """Create Flask app for testing"""
    # Create temporary directory for test contexts
    temp_dir = tempfile.mkdtemp()
    
    app = create_app()
    app.config['TESTING'] = True
    app.config['API_KEY'] = 'test-api-key'
    app.config['CONTEXT_DIR'] = temp_dir
    
    yield app
    
    # Cleanup temp directory
    import shutil
    shutil.rmtree(temp_dir, ignore_errors=True)


@pytest.fixture
def client(app):
    """Create test client"""
    return app.test_client()


@pytest.fixture
def sample_session_context(app):
    """Create a sample session context file"""
    session_id = 'test-session-123'
    context_data = {
        "session_id": session_id,
        "timestamp": "2025-11-15T10:30:00Z",
        "task": "PSU_Install",
        "step": "4",
        "gaze_vector": {"x": 0.5, "y": -0.2, "z": 0.8},
        "image_analysis": "The image shows a server chassis with visible power connectors.",
        "instruction": {
            "steps": [
                "Locate the 8-pin PDU cable on the left side.",
                "Align the connector with the J_PWR_1 socket.",
                "Press firmly until you hear a click."
            ],
            "target_id": "J_PWR_1",
            "haptic_cue": "guide_to_target"
        }
    }
    
    # Write context file
    context_dir = Path(app.config['CONTEXT_DIR'])
    context_dir.mkdir(parents=True, exist_ok=True)
    context_file = context_dir / f"{session_id}.json"
    
    with open(context_file, 'w') as f:
        json.dump(context_data, f)
    
    return session_id, context_data


class TestAskEndpoint:
    """Test suite for /ask endpoint"""
    
    def test_missing_authorization_header(self, client):
        """Test that missing Authorization header returns 401"""
        data = {
            'session_id': 'test-session-123',
            'question': 'What should I do next?'
        }
        
        response = client.post('/ask', 
                             data=json.dumps(data),
                             content_type='application/json')
        
        assert response.status_code == 401
        response_data = json.loads(response.data)
        assert response_data['status'] == 'error'
        assert response_data['error_code'] == 'AUTH_FAILED'
    
    def test_invalid_api_key(self, client):
        """Test that invalid API key returns 401"""
        headers = {'Authorization': 'Bearer wrong-key'}
        data = {
            'session_id': 'test-session-123',
            'question': 'What should I do next?'
        }
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 401
        response_data = json.loads(response.data)
        assert response_data['error_code'] == 'AUTH_FAILED'
    
    def test_non_json_request(self, client):
        """Test that non-JSON request returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        
        response = client.post('/ask',
                             data='not json',
                             headers=headers,
                             content_type='text/plain')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'json' in response_data['message'].lower()
    
    def test_missing_session_id(self, client):
        """Test that missing session_id returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'question': 'What should I do next?'
            # Missing session_id
        }
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'session_id' in response_data['message'].lower()
    
    def test_missing_question(self, client):
        """Test that missing question returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'session_id': 'test-session-123'
            # Missing question
        }
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'question' in response_data['message'].lower()
    
    def test_non_existent_session(self, client):
        """Test that non-existent session_id returns 404"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'session_id': 'non-existent-session',
            'question': 'What should I do next?'
        }
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 404
        response_data = json.loads(response.data)
        assert response_data['status'] == 'error'
        assert response_data['error_code'] == 'NOT_FOUND'
    
    @patch('app.routes.ask.ChatGoogleGenerativeAI')
    def test_successful_follow_up_question(self, mock_llm_class, client, app, sample_session_context):
        """Test successful follow-up question returns proper JSON structure"""
        session_id, context_data = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'session_id': session_id,
            'question': 'What if the cable does not fit?'
        }
        
        # Mock Gemini response
        mock_llm = Mock()
        mock_response = Mock()
        mock_response.content = "If the cable doesn't fit, check the orientation and ensure you're using the correct 8-pin connector."
        mock_llm.invoke.return_value = mock_response
        mock_llm_class.return_value = mock_llm
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 200
        response_data = json.loads(response.data)
        
        # Verify response structure
        assert response_data['status'] == 'success'
        assert response_data['session_id'] == session_id
        assert 'answer_steps' in response_data
        assert isinstance(response_data['answer_steps'], list)
        assert len(response_data['answer_steps']) > 0
        
        # Verify context is included
        assert 'context' in response_data
        assert response_data['context']['task'] == 'PSU_Install'
        assert response_data['context']['step'] == '4'
        assert 'previous_instruction' in response_data['context']
        
        # Verify LLM was called
        mock_llm.invoke.assert_called_once()
        
        # Verify follow-up Q&A was saved to context file
        from pathlib import Path
        context_file = Path(app.config['CONTEXT_DIR']) / f"{session_id}.json"
        assert context_file.exists()
        
        with open(context_file, 'r') as f:
            updated_context = json.load(f)
        
        assert 'follow_up_qa' in updated_context
        assert len(updated_context['follow_up_qa']) == 1
        assert updated_context['follow_up_qa'][0]['question'] == 'What if the cable does not fit?'
        assert 'answer_steps' in updated_context['follow_up_qa'][0]
        assert isinstance(updated_context['follow_up_qa'][0]['answer_steps'], list)
        assert len(updated_context['follow_up_qa'][0]['answer_steps']) > 0
        assert 'timestamp' in updated_context['follow_up_qa'][0]
    
    @patch('app.routes.ask.ChatGoogleGenerativeAI')
    def test_response_includes_previous_context(self, mock_llm_class, client, app, sample_session_context):
        """Test that response includes context from previous session"""
        session_id, context_data = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'session_id': session_id,
            'question': 'Can you clarify step 2?'
        }
        
        # Mock Gemini response
        mock_llm = Mock()
        mock_response = Mock()
        mock_response.content = "Step 2 means to align the 8-pin connector carefully with the J_PWR_1 socket before inserting."
        mock_llm.invoke.return_value = mock_response
        mock_llm_class.return_value = mock_llm
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 200
        response_data = json.loads(response.data)
        
        # Verify previous context is included
        assert response_data['context']['task'] == context_data['task']
        assert response_data['context']['step'] == context_data['step']
        
        # Verify the prompt included previous instruction
        call_args = mock_llm.invoke.call_args
        prompt_message = call_args[0][0][0]
        prompt_content = prompt_message.content
        
        # Check that previous context was included in prompt
        assert 'PSU_Install' in prompt_content
        assert 'Locate the 8-pin PDU cable' in prompt_content
    
    @patch('app.routes.ask.ChatGoogleGenerativeAI')
    def test_llm_error_handling(self, mock_llm_class, client, app, sample_session_context):
        """Test that LLM errors are handled properly"""
        session_id, _ = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'session_id': session_id,
            'question': 'What should I do?'
        }
        
        # Mock LLM to raise an exception
        mock_llm = Mock()
        mock_llm.invoke.side_effect = Exception('Gemini API error')
        mock_llm_class.return_value = mock_llm
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 500
        response_data = json.loads(response.data)
        assert response_data['status'] == 'error'
        assert response_data['error_code'] == 'LLM_ERROR'
        assert 'Gemini API error' in response_data['error']
    
    @patch('app.routes.ask.ChatGoogleGenerativeAI')
    def test_gemini_api_key_not_configured(self, mock_llm_class, client, app, sample_session_context):
        """Test error when GEMINI_API_KEY is not configured"""
        session_id, _ = sample_session_context
        
        # Remove API key from config
        original_key = app.config.get('GEMINI_API_KEY')
        app.config['GEMINI_API_KEY'] = None
        
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'session_id': session_id,
            'question': 'What should I do?'
        }
        
        response = client.post('/ask',
                             data=json.dumps(data),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 500
        response_data = json.loads(response.data)
        assert 'GEMINI_API_KEY' in response_data['error']
        
        # Restore API key
        app.config['GEMINI_API_KEY'] = original_key
    
    # Voice input tests
    
    @patch('app.routes.ask.transcribe_audio')
    @patch('app.routes.ask.ChatGoogleGenerativeAI')
    def test_ask_with_audio_file_returns_transcribed_answer(self, mock_llm_class, mock_transcribe, client, app, sample_session_context):
        """Test /ask with audio file returns transcribed answer"""
        session_id, context_data = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Create a mock WAV audio file
        audio_data = b'RIFF' + b'\x00' * 4 + b'WAVE' + b'\x00' * 100
        audio_file = io.BytesIO(audio_data)
        audio_file.name = 'question.wav'
        
        # Mock transcription
        mock_transcribe.return_value = (True, "What if the cable doesn't fit?", "")
        
        # Mock Gemini response
        mock_llm = Mock()
        mock_response = Mock()
        mock_response.content = "1. Check the orientation of the connector\n2. Ensure you're using the correct 8-pin cable\n3. Do not force the connection"
        mock_llm.invoke.return_value = mock_response
        mock_llm_class.return_value = mock_llm
        
        # Send multipart request with audio
        data = {
            'session_id': session_id,
            'audio': (audio_file, 'question.wav', 'audio/wav')
        }
        
        response = client.post('/ask',
                             data=data,
                             headers=headers,
                             content_type='multipart/form-data')
        
        assert response.status_code == 200
        response_data = json.loads(response.data)
        
        # Verify response structure
        assert response_data['status'] == 'success'
        assert response_data['session_id'] == session_id
        assert 'answer_steps' in response_data
        assert isinstance(response_data['answer_steps'], list)
        assert len(response_data['answer_steps']) > 0
        
        # Verify transcribed question is included
        assert 'transcribed_question' in response_data
        assert response_data['transcribed_question'] == "What if the cable doesn't fit?"
        
        # Verify transcribe_audio was called
        mock_transcribe.assert_called_once()
        call_args = mock_transcribe.call_args
        assert call_args[0][1] == 'audio/wav'  # content_type
        
        # Verify LLM was called with transcribed text
        mock_llm.invoke.assert_called_once()
    
    @patch('app.routes.ask.validate_audio')
    def test_ask_with_invalid_audio_returns_400(self, mock_validate, client, app, sample_session_context):
        """Test /ask with invalid audio returns 400"""
        session_id, _ = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Create a mock invalid audio file
        audio_data = b'invalid audio data'
        audio_file = io.BytesIO(audio_data)
        audio_file.name = 'invalid.wav'
        
        # Mock validation to fail
        mock_validate.return_value = (False, "Invalid WAV file format")
        
        # Send multipart request with invalid audio
        data = {
            'session_id': session_id,
            'audio': (audio_file, 'invalid.wav', 'audio/wav')
        }
        
        response = client.post('/ask',
                             data=data,
                             headers=headers,
                             content_type='multipart/form-data')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'Invalid WAV file format' in response_data['message']
    
    @patch('app.routes.ask.validate_audio')
    def test_ask_with_oversized_audio_returns_413(self, mock_validate, client, app, sample_session_context):
        """Test /ask with oversized audio returns 413"""
        session_id, _ = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Create a mock oversized audio file
        audio_data = b'RIFF' + b'\x00' * 4 + b'WAVE' + b'\x00' * 100
        audio_file = io.BytesIO(audio_data)
        audio_file.name = 'large.wav'
        
        # Mock validation to fail with size error
        mock_validate.return_value = (False, "Audio file too large. Maximum 10MB")
        
        # Send multipart request with oversized audio
        data = {
            'session_id': session_id,
            'audio': (audio_file, 'large.wav', 'audio/wav')
        }
        
        response = client.post('/ask',
                             data=data,
                             headers=headers,
                             content_type='multipart/form-data')
        
        assert response.status_code == 413
        response_data = json.loads(response.data)
        # The error handler returns a generic message, but status code is correct
        assert response_data['status'] == 'error'
        assert response_data['error_code'] == 'IMAGE_TOO_LARGE'
    
    @patch('app.routes.ask.ChatGoogleGenerativeAI')
    def test_backward_compatibility_with_text_only_requests(self, mock_llm_class, client, app, sample_session_context):
        """Test backward compatibility with text-only requests (no audio)"""
        session_id, context_data = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Test 1: JSON request (original format)
        data_json = {
            'session_id': session_id,
            'question': 'What is the next step?'
        }
        
        # Mock Gemini response
        mock_llm = Mock()
        mock_response = Mock()
        mock_response.content = "1. Connect the cable\n2. Secure the connection\n3. Verify the LED indicator"
        mock_llm.invoke.return_value = mock_response
        mock_llm_class.return_value = mock_llm
        
        response = client.post('/ask',
                             data=json.dumps(data_json),
                             headers=headers,
                             content_type='application/json')
        
        assert response.status_code == 200
        response_data = json.loads(response.data)
        assert response_data['status'] == 'success'
        assert 'answer_steps' in response_data
        # Should NOT have transcribed_question for text-only
        assert 'transcribed_question' not in response_data
        
        # Test 2: Multipart request with text question (no audio)
        data_form = {
            'session_id': session_id,
            'question': 'Can you clarify step 2?'
        }
        
        response = client.post('/ask',
                             data=data_form,
                             headers=headers,
                             content_type='multipart/form-data')
        
        assert response.status_code == 200
        response_data = json.loads(response.data)
        assert response_data['status'] == 'success'
        assert 'answer_steps' in response_data
        # Should NOT have transcribed_question for text-only
        assert 'transcribed_question' not in response_data
    
    @patch('app.routes.ask.transcribe_audio')
    def test_ask_with_audio_transcription_failure(self, mock_transcribe, client, app, sample_session_context):
        """Test /ask handles audio transcription failures gracefully"""
        session_id, _ = sample_session_context
        
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Create a mock audio file
        audio_data = b'RIFF' + b'\x00' * 4 + b'WAVE' + b'\x00' * 100
        audio_file = io.BytesIO(audio_data)
        audio_file.name = 'question.wav'
        
        # Mock transcription to fail
        mock_transcribe.return_value = (False, "", "No speech detected in audio")
        
        # Send multipart request with audio
        data = {
            'session_id': session_id,
            'audio': (audio_file, 'question.wav', 'audio/wav')
        }
        
        response = client.post('/ask',
                             data=data,
                             headers=headers,
                             content_type='multipart/form-data')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'transcription failed' in response_data['message'].lower()
        assert 'No speech detected' in response_data['message']
