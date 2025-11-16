"""
Integration tests for the /assist endpoint
"""
import pytest
import json
import io
from unittest.mock import Mock, patch
from PIL import Image
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from app.main import create_app


@pytest.fixture
def app():
    """Create Flask app for testing"""
    app = create_app()
    app.config['TESTING'] = True
    app.config['API_KEY'] = 'test-api-key'
    return app


@pytest.fixture
def client(app):
    """Create test client"""
    return app.test_client()


@pytest.fixture
def sample_image():
    """Create a sample test image"""
    img = Image.new('RGB', (100, 100), color='red')
    img_bytes = io.BytesIO()
    img.save(img_bytes, format='JPEG')
    img_bytes.seek(0)
    return img_bytes


@pytest.fixture
def valid_form_data(sample_image):
    """Create valid form data for /assist endpoint"""
    return {
        'snapshot': (sample_image, 'test.jpg', 'image/jpeg'),
        'task_step': '4',
        'current_task': 'PSU_Install',
        'gaze_vector': json.dumps({"x": 0.5, "y": -0.2, "z": 0.8}),
        'session_id': 'test-session-123'
    }


class TestAssistEndpoint:
    """Test suite for /assist endpoint"""
    
    def test_missing_authorization_header(self, client, valid_form_data):
        """Test that missing Authorization header returns 401"""
        response = client.post('/assist', data=valid_form_data, content_type='multipart/form-data')
        
        assert response.status_code == 401
        data = json.loads(response.data)
        assert data['status'] == 'error'
        assert data['error_code'] == 'AUTH_FAILED'
    
    def test_invalid_api_key(self, client, valid_form_data):
        """Test that invalid API key returns 401"""
        headers = {'Authorization': 'Bearer wrong-key'}
        response = client.post('/assist', data=valid_form_data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 401
        data = json.loads(response.data)
        assert data['error_code'] == 'AUTH_FAILED'
    
    def test_missing_snapshot(self, client):
        """Test that missing snapshot file returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'task_step': '4',
            'current_task': 'PSU_Install',
            'gaze_vector': json.dumps({"x": 0.5, "y": -0.2, "z": 0.8})
        }
        
        response = client.post('/assist', data=data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'snapshot' in response_data['message'].lower()
    
    def test_missing_required_fields(self, client, sample_image):
        """Test that missing required fields returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'snapshot': (sample_image, 'test.jpg', 'image/jpeg'),
            # Missing task_step, current_task, gaze_vector
        }
        
        response = client.post('/assist', data=data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 400
    
    def test_invalid_image_type(self, client):
        """Test that invalid image type returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Create a text file instead of image
        text_file = io.BytesIO(b"This is not an image")
        
        data = {
            'snapshot': (text_file, 'test.txt', 'text/plain'),
            'task_step': '4',
            'current_task': 'PSU_Install',
            'gaze_vector': json.dumps({"x": 0.5, "y": -0.2, "z": 0.8})
        }
        
        response = client.post('/assist', data=data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'image' in response_data['message'].lower()
    
    def test_invalid_gaze_vector_json(self, client, sample_image):
        """Test that invalid gaze_vector JSON returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'snapshot': (sample_image, 'test.jpg', 'image/jpeg'),
            'task_step': '4',
            'current_task': 'PSU_Install',
            'gaze_vector': 'not valid json'
        }
        
        response = client.post('/assist', data=data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 400
        response_data = json.loads(response.data)
        assert 'gaze_vector' in response_data['message'].lower()
    
    def test_gaze_vector_missing_fields(self, client, sample_image):
        """Test that gaze_vector missing x, y, z returns 400"""
        headers = {'Authorization': 'Bearer test-api-key'}
        data = {
            'snapshot': (sample_image, 'test.jpg', 'image/jpeg'),
            'task_step': '4',
            'current_task': 'PSU_Install',
            'gaze_vector': json.dumps({"x": 0.5, "y": -0.2})  # Missing z
        }
        
        response = client.post('/assist', data=data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 400
    
    @patch('app.routes.assist.compress_image')
    def test_successful_request_with_mocked_workflow(self, mock_compress, client, app, valid_form_data):
        """Test successful request with mocked workflow"""
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Mock the workflow
        mock_workflow = Mock()
        mock_workflow.run.return_value = {
            'instruction_text': 'Locate the 8-pin PDU cable',
            'target_id': 'J_PWR_1',
            'haptic_cue': 'guide_to_target',
            'session_id': 'test-session-123'
        }
        app.workflow = mock_workflow
        
        # Mock compress_image to return original
        mock_compress.return_value = b'compressed_image_data'
        
        response = client.post('/assist', data=valid_form_data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 200
        data = json.loads(response.data)
        
        # Verify response structure
        assert data['status'] == 'success'
        assert data['session_id'] == 'test-session-123'
        assert 'instruction_id' in data
        assert data['instruction_steps'] == ['Locate the 8-pin PDU cable']
        assert data['target_id'] == 'J_PWR_1'
        assert data['haptic_cue'] == 'guide_to_target'
        
        # Verify workflow was called
        mock_workflow.run.assert_called_once()
    
    @patch('app.routes.assist.compress_image')
    def test_session_id_generation(self, mock_compress, client, app, sample_image):
        """Test that session_id is generated if not provided"""
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Mock the workflow
        mock_workflow = Mock()
        mock_workflow.run.return_value = {
            'instruction_text': 'Test instruction',
            'target_id': 'TEST_1',
            'haptic_cue': 'none'
        }
        app.workflow = mock_workflow
        mock_compress.return_value = b'compressed_image_data'
        
        data = {
            'snapshot': (sample_image, 'test.jpg', 'image/jpeg'),
            'task_step': '1',
            'current_task': 'Test_Task',
            'gaze_vector': json.dumps({"x": 0, "y": 0, "z": 0})
            # No session_id provided
        }
        
        response = client.post('/assist', data=data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 200
        response_data = json.loads(response.data)
        
        # Verify session_id was generated
        assert 'session_id' in response_data
        assert len(response_data['session_id']) > 0
    
    def test_workflow_error_handling(self, client, app, valid_form_data):
        """Test that workflow errors are handled properly"""
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Mock the workflow to return an error
        mock_workflow = Mock()
        mock_workflow.run.return_value = {
            'error': 'Gemini API failed'
        }
        app.workflow = mock_workflow
        
        response = client.post('/assist', data=valid_form_data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 500
        data = json.loads(response.data)
        assert data['status'] == 'error'
        assert 'Gemini API failed' in data['error']
    
    def test_workflow_not_initialized(self, client, app, valid_form_data):
        """Test error when workflow is not initialized"""
        headers = {'Authorization': 'Bearer test-api-key'}
        
        # Set workflow to None
        app.workflow = None
        
        response = client.post('/assist', data=valid_form_data, headers=headers, content_type='multipart/form-data')
        
        assert response.status_code == 500
        data = json.loads(response.data)
        assert 'workflow' in data['error'].lower()
