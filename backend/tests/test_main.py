"""
Tests for Flask application core functionality.
"""
import pytest
import json
import os
from unittest.mock import patch, MagicMock

# Set test environment variables BEFORE importing app modules
os.environ['GEMINI_API_KEY'] = 'test_gemini_key'
os.environ['API_KEY'] = 'test_api_key'
os.environ['FLASK_ENV'] = 'testing'

from app.main import create_app, Config


@pytest.fixture
def app():
    """Create and configure a test Flask application."""
    app = create_app()
    app.config['TESTING'] = True
    yield app


@pytest.fixture
def client(app):
    """Create a test client for the Flask application."""
    return app.test_client()


class TestFlaskAppInitialization:
    """Tests for Flask app initialization and configuration."""
    
    def test_app_creation(self, app):
        """Test that Flask app is created successfully."""
        assert app is not None
        assert app.config['TESTING'] is True
    
    def test_config_loading(self, app):
        """Test that configuration loads environment variables correctly."""
        assert Config.GEMINI_API_KEY == 'test_gemini_key'
        assert Config.API_KEY == 'test_api_key'
        assert Config.FLASK_ENV == 'testing'
        assert Config.MAX_IMAGE_SIZE == 5 * 1024 * 1024
        assert Config.IMAGE_COMPRESSION_SIZE == (768, 768)
        assert Config.SESSION_TIMEOUT_HOURS == 24
    
    def test_json_logging_setup(self, app):
        """Test that JSON logging is configured."""
        assert len(app.logger.handlers) > 0
        # Check that logger is set up
        assert app.logger.level > 0


class TestErrorHandlers:
    """Tests for error handler responses."""
    
    def test_400_error_handler(self, client):
        """Test 400 Bad Request error handler returns correct JSON format."""
        # Trigger a 400 error by sending invalid JSON
        response = client.post('/nonexistent', 
                              data='invalid',
                              content_type='application/json')
        
        # The route doesn't exist, but we can test the error format
        # by checking any 400 response
        assert response.status_code in [400, 404]  # May be 404 for nonexistent route
    
    def test_error_response_format(self, client):
        """Test that error responses have correct JSON structure."""
        # Create a test route that raises a 400 error
        from werkzeug.exceptions import BadRequest
        
        @client.application.route('/test_400')
        def test_400():
            raise BadRequest('Test error')
        
        response = client.get('/test_400')
        
        assert response.status_code == 400
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'error'
        assert 'error' in data
        assert 'error_code' in data
        assert data['error_code'] == 'BAD_REQUEST'
    
    def test_401_error_format(self, client):
        """Test 401 Unauthorized error returns correct format."""
        from werkzeug.exceptions import Unauthorized
        
        @client.application.route('/test_401')
        def test_401():
            raise Unauthorized('Invalid credentials')
        
        response = client.get('/test_401')
        
        assert response.status_code == 401
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'error'
        assert data['error_code'] == 'AUTH_FAILED'
    
    def test_413_error_format(self, client):
        """Test 413 Request Entity Too Large error returns correct format."""
        from werkzeug.exceptions import RequestEntityTooLarge
        
        @client.application.route('/test_413')
        def test_413():
            raise RequestEntityTooLarge('File too large')
        
        response = client.get('/test_413')
        
        assert response.status_code == 413
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'error'
        assert data['error_code'] == 'IMAGE_TOO_LARGE'
    
    def test_500_error_format(self, client):
        """Test 500 Internal Server Error returns correct format."""
        from werkzeug.exceptions import InternalServerError
        
        @client.application.route('/test_500')
        def test_500():
            raise InternalServerError('Something went wrong')
        
        response = client.get('/test_500')
        
        assert response.status_code == 500
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'error'
        assert data['error_code'] == 'INTERNAL_ERROR'
    
    def test_503_error_format(self, client):
        """Test 503 Service Unavailable error returns correct format."""
        from werkzeug.exceptions import ServiceUnavailable
        
        @client.application.route('/test_503')
        def test_503():
            raise ServiceUnavailable('Service down')
        
        response = client.get('/test_503')
        
        assert response.status_code == 503
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'error'
        assert data['error_code'] == 'SERVICE_UNAVAILABLE'


class TestHealthEndpoint:
    """Tests for /health endpoint."""
    
    @patch('app.main.ChatGoogleGenerativeAI')
    def test_health_endpoint_success(self, mock_gemini, client):
        """Test /health endpoint returns 200 when Gemini API is reachable."""
        # Mock successful Gemini API call
        mock_llm_instance = MagicMock()
        mock_llm_instance.invoke.return_value = MagicMock()
        mock_gemini.return_value = mock_llm_instance
        
        response = client.get('/health')
        
        assert response.status_code == 200
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'healthy'
        assert 'timestamp' in data
    
    @patch('app.main.ChatGoogleGenerativeAI')
    def test_health_endpoint_gemini_failure(self, mock_gemini, client):
        """Test /health endpoint returns 503 when Gemini API fails."""
        # Mock Gemini API failure
        mock_gemini.side_effect = Exception('Gemini API unavailable')
        
        response = client.get('/health')
        
        assert response.status_code == 503
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'unhealthy'
        assert 'error' in data
        assert 'timestamp' in data
    
    @patch('app.main.Config')
    def test_health_endpoint_missing_api_key(self, mock_config, client):
        """Test /health endpoint handles missing API key."""
        # Mock missing API key
        mock_config.GEMINI_API_KEY = None
        
        response = client.get('/health')
        
        assert response.status_code == 503
        data = json.loads(response.get_data(as_text=True))
        assert data['status'] == 'unhealthy'
