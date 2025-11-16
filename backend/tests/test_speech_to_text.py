"""
Tests for speech-to-text utilities.
"""
import pytest
from unittest.mock import patch, MagicMock
from google.cloud import speech
from app.utils.speech_to_text import transcribe_audio, get_encoding_from_content_type


class TestGetEncodingFromContentType:
    """Tests for content type to encoding mapping."""
    
    def test_wav_encoding(self):
        """Test WAV content type maps to LINEAR16."""
        encoding = get_encoding_from_content_type('audio/wav')
        assert encoding == speech.RecognitionConfig.AudioEncoding.LINEAR16
    
    def test_mp3_encoding(self):
        """Test MP3 content type maps to MP3."""
        encoding = get_encoding_from_content_type('audio/mpeg')
        assert encoding == speech.RecognitionConfig.AudioEncoding.MP3
    
    def test_ogg_encoding(self):
        """Test OGG content type maps to OGG_OPUS."""
        encoding = get_encoding_from_content_type('audio/ogg')
        assert encoding == speech.RecognitionConfig.AudioEncoding.OGG_OPUS
    
    def test_flac_encoding(self):
        """Test FLAC content type maps to FLAC."""
        encoding = get_encoding_from_content_type('audio/flac')
        assert encoding == speech.RecognitionConfig.AudioEncoding.FLAC
    
    def test_unknown_encoding_defaults_to_linear16(self):
        """Test unknown content type defaults to LINEAR16."""
        encoding = get_encoding_from_content_type('audio/unknown')
        assert encoding == speech.RecognitionConfig.AudioEncoding.LINEAR16


class TestTranscribeAudio:
    """Tests for audio transcription."""
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_success(self, mock_speech_client):
        """Test successful audio transcription."""
        # Mock the Speech API response
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        # Create mock response with transcription
        mock_result = MagicMock()
        mock_alternative = MagicMock()
        mock_alternative.transcript = "Hello, this is a test."
        mock_result.alternatives = [mock_alternative]
        
        mock_response = MagicMock()
        mock_response.results = [mock_result]
        
        mock_client.recognize.return_value = mock_response
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert success
        assert text == "Hello, this is a test."
        assert error == ""
        
        # Verify API was called correctly
        mock_client.recognize.assert_called_once()
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_multiple_results(self, mock_speech_client):
        """Test transcription with multiple results combines them."""
        # Mock the Speech API response with multiple results
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        # Create mock response with multiple transcription results
        mock_result1 = MagicMock()
        mock_alternative1 = MagicMock()
        mock_alternative1.transcript = "First sentence."
        mock_result1.alternatives = [mock_alternative1]
        
        mock_result2 = MagicMock()
        mock_alternative2 = MagicMock()
        mock_alternative2.transcript = "Second sentence."
        mock_result2.alternatives = [mock_alternative2]
        
        mock_response = MagicMock()
        mock_response.results = [mock_result1, mock_result2]
        
        mock_client.recognize.return_value = mock_response
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert success
        assert text == "First sentence. Second sentence."
        assert error == ""
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_no_speech_detected(self, mock_speech_client):
        """Test transcription when no speech is detected."""
        # Mock the Speech API response with no results
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_response = MagicMock()
        mock_response.results = []
        
        mock_client.recognize.return_value = mock_response
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "No speech detected" in error
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_empty_transcript(self, mock_speech_client):
        """Test transcription when result is empty."""
        # Mock the Speech API response with empty transcript
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_result = MagicMock()
        mock_alternative = MagicMock()
        mock_alternative.transcript = "   "  # Empty/whitespace only
        mock_result.alternatives = [mock_alternative]
        
        mock_response = MagicMock()
        mock_response.results = [mock_result]
        
        mock_client.recognize.return_value = mock_response
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "No speech detected" in error
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_invalid_argument_error(self, mock_speech_client):
        """Test transcription handles INVALID_ARGUMENT error."""
        # Mock the Speech API to raise an error
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_client.recognize.side_effect = Exception('INVALID_ARGUMENT: Invalid audio format')
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "Invalid audio format or corrupted file" in error
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_unauthenticated_error(self, mock_speech_client):
        """Test transcription handles UNAUTHENTICATED error."""
        # Mock the Speech API to raise an authentication error
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_client.recognize.side_effect = Exception('UNAUTHENTICATED: Invalid credentials')
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "authentication failed" in error.lower()
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_permission_denied_error(self, mock_speech_client):
        """Test transcription handles PERMISSION_DENIED error."""
        # Mock the Speech API to raise a permission error
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_client.recognize.side_effect = Exception('PERMISSION_DENIED: Access denied')
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "access denied" in error.lower()
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_quota_exceeded_error(self, mock_speech_client):
        """Test transcription handles RESOURCE_EXHAUSTED error."""
        # Mock the Speech API to raise a quota error
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_client.recognize.side_effect = Exception('RESOURCE_EXHAUSTED: Quota exceeded')
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "quota exceeded" in error.lower()
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_generic_error(self, mock_speech_client):
        """Test transcription handles generic errors."""
        # Mock the Speech API to raise a generic error
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_client.recognize.side_effect = Exception('Unknown error occurred')
        
        # Test transcription
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav')
        
        assert not success
        assert text == ""
        assert "Transcription failed" in error
        assert "Unknown error occurred" in error
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_different_formats(self, mock_speech_client):
        """Test transcription works with different audio formats."""
        # Mock the Speech API response
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_result = MagicMock()
        mock_alternative = MagicMock()
        mock_alternative.transcript = "Test transcription."
        mock_result.alternatives = [mock_alternative]
        
        mock_response = MagicMock()
        mock_response.results = [mock_result]
        
        mock_client.recognize.return_value = mock_response
        
        # Test different formats
        formats = ['audio/wav', 'audio/mpeg', 'audio/ogg', 'audio/flac']
        audio_bytes = b'fake audio data'
        
        for content_type in formats:
            success, text, error = transcribe_audio(audio_bytes, content_type)
            assert success, f"Failed for {content_type}"
            assert text == "Test transcription."
            assert error == ""
    
    @patch('app.utils.speech_to_text.speech.SpeechClient')
    def test_transcribe_audio_custom_language(self, mock_speech_client):
        """Test transcription with custom language code."""
        # Mock the Speech API response
        mock_client = MagicMock()
        mock_speech_client.return_value = mock_client
        
        mock_result = MagicMock()
        mock_alternative = MagicMock()
        mock_alternative.transcript = "Bonjour."
        mock_result.alternatives = [mock_alternative]
        
        mock_response = MagicMock()
        mock_response.results = [mock_result]
        
        mock_client.recognize.return_value = mock_response
        
        # Test transcription with French language
        audio_bytes = b'fake audio data'
        success, text, error = transcribe_audio(audio_bytes, 'audio/wav', language_code='fr-FR')
        
        assert success
        assert text == "Bonjour."
        assert error == ""
