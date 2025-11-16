"""
Main Flask application with configuration, logging, and error handlers.
"""
import os
import json
import logging
from datetime import datetime
from flask import Flask, jsonify, request
from dotenv import load_dotenv
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_core.messages import HumanMessage

# Import VRContextWorkflow - handle both local and Docker paths
import sys
import os
# Add parent directory to path for imports
parent_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if parent_dir not in sys.path:
    sys.path.insert(0, parent_dir)

try:
    from llm import VRContextWorkflow
except ImportError:
    # If running from Docker, try absolute import
    sys.path.insert(0, '/app')
    from llm import VRContextWorkflow

# Load environment variables
load_dotenv()


class Config:
    """Configuration class to load environment variables."""
    
    GEMINI_API_KEY = os.getenv('GEMINI_API_KEY')
    API_KEY = os.getenv('API_KEY')
    FLASK_ENV = os.getenv('FLASK_ENV', 'production')
    LOG_LEVEL = os.getenv('LOG_LEVEL', 'INFO')
    MAX_IMAGE_SIZE = int(os.getenv('MAX_IMAGE_SIZE', 5 * 1024 * 1024))  # 5MB default
    IMAGE_COMPRESSION_SIZE = tuple(map(int, os.getenv('IMAGE_COMPRESSION_SIZE', '768,768').split(',')))
    SESSION_TIMEOUT_HOURS = int(os.getenv('SESSION_TIMEOUT_HOURS', 24))
    CONTEXT_DIR = os.getenv('CONTEXT_DIR', 'contexts')


class JSONFormatter(logging.Formatter):
    """Custom JSON formatter for structured logging."""
    
    def format(self, record):
        log_data = {
            'timestamp': datetime.utcnow().isoformat() + 'Z',
            'level': record.levelname,
            'message': record.getMessage(),
            'module': record.module,
            'function': record.funcName,
            'line': record.lineno
        }
        
        # Add extra fields if present
        if hasattr(record, 'session_id'):
            log_data['session_id'] = record.session_id
        if hasattr(record, 'endpoint'):
            log_data['endpoint'] = record.endpoint
        if hasattr(record, 'task'):
            log_data['task'] = record.task
        if hasattr(record, 'step'):
            log_data['step'] = record.step
        if hasattr(record, 'duration_ms'):
            log_data['duration_ms'] = record.duration_ms
        if hasattr(record, 'status'):
            log_data['status'] = record.status
        
        # Add exception info if present
        if record.exc_info:
            log_data['exception'] = self.formatException(record.exc_info)
        
        return json.dumps(log_data)


def setup_logging(app):
    """Set up JSON logging for the application."""
    # Remove default handlers
    app.logger.handlers.clear()
    
    # Create console handler with JSON formatter
    handler = logging.StreamHandler()
    handler.setFormatter(JSONFormatter())
    
    # Set log level
    log_level = getattr(logging, Config.LOG_LEVEL.upper(), logging.INFO)
    handler.setLevel(log_level)
    app.logger.setLevel(log_level)
    
    # Add handler to app logger
    app.logger.addHandler(handler)
    
    # Prevent propagation to avoid duplicate logs
    app.logger.propagate = False


def create_app():
    """Create and configure the Flask application."""
    app = Flask(__name__)
    app.config.from_object(Config)
    
    # Set up JSON logging
    setup_logging(app)
    
    # Initialize VRContextWorkflow
    if Config.GEMINI_API_KEY:
        app.workflow = VRContextWorkflow(api_key=Config.GEMINI_API_KEY)
        app.logger.info('VRContextWorkflow initialized')
    else:
        app.logger.warning('GEMINI_API_KEY not set, workflow not initialized')
        app.workflow = None
    
    # Register error handlers
    register_error_handlers(app)
    
    # Register routes
    register_routes(app)
    
    app.logger.info('Flask application initialized', extra={
        'flask_env': Config.FLASK_ENV,
        'log_level': Config.LOG_LEVEL
    })
    
    return app


def register_error_handlers(app):
    """Register error handlers for common HTTP status codes."""
    
    @app.errorhandler(400)
    def bad_request(error):
        """Handle 400 Bad Request errors."""
        app.logger.warning(f'Bad request: {str(error)}')
        return jsonify({
            'status': 'error',
            'error': 'Bad request',
            'error_code': 'BAD_REQUEST',
            'message': str(error.description) if hasattr(error, 'description') else 'Invalid request data'
        }), 400
    
    @app.errorhandler(401)
    def unauthorized(error):
        """Handle 401 Unauthorized errors."""
        app.logger.warning(f'Unauthorized access attempt: {str(error)}')
        return jsonify({
            'status': 'error',
            'error': 'Unauthorized',
            'error_code': 'AUTH_FAILED',
            'message': 'Invalid or missing API key'
        }), 401
    
    @app.errorhandler(404)
    def not_found(error):
        """Handle 404 Not Found errors."""
        app.logger.warning(f'Resource not found: {str(error)}')
        return jsonify({
            'status': 'error',
            'error': 'Not found',
            'error_code': 'NOT_FOUND',
            'message': str(error.description) if hasattr(error, 'description') else 'Resource not found'
        }), 404
    
    @app.errorhandler(413)
    def request_entity_too_large(error):
        """Handle 413 Request Entity Too Large errors."""
        app.logger.warning(f'Request too large: {str(error)}')
        return jsonify({
            'status': 'error',
            'error': 'Request entity too large',
            'error_code': 'IMAGE_TOO_LARGE',
            'message': f'Image exceeds maximum size of {Config.MAX_IMAGE_SIZE} bytes'
        }), 413
    
    @app.errorhandler(500)
    def internal_server_error(error):
        """Handle 500 Internal Server Error."""
        app.logger.error(f'Internal server error: {str(error)}', exc_info=True)
        return jsonify({
            'status': 'error',
            'error': 'Internal server error',
            'error_code': 'INTERNAL_ERROR',
            'message': 'An unexpected error occurred'
        }), 500
    
    @app.errorhandler(503)
    def service_unavailable(error):
        """Handle 503 Service Unavailable errors."""
        app.logger.error(f'Service unavailable: {str(error)}')
        return jsonify({
            'status': 'error',
            'error': 'Service unavailable',
            'error_code': 'SERVICE_UNAVAILABLE',
            'message': 'External service is currently unavailable'
        }), 503


def register_routes(app):
    """Register application routes."""
    
    # Import and register assist route
    from app.routes.assist import register_assist_route
    register_assist_route(app)
    
    # Import and register ask route
    from app.routes.ask import register_ask_route
    register_ask_route(app)
    
    @app.route('/health', methods=['GET'])
    def health():
        """
        Health check endpoint that tests Gemini API connectivity.
        Returns 200 if healthy, 503 if Gemini API is unreachable.
        """
        try:
            # Quick Gemini API connectivity check
            if not Config.GEMINI_API_KEY:
                raise ValueError("GEMINI_API_KEY not configured")
            
            llm = ChatGoogleGenerativeAI(
                model="gemini-2.5-flash",
                google_api_key=Config.GEMINI_API_KEY,
                temperature=0
            )
            
            # Simple test invocation
            llm.invoke([HumanMessage(content="test")])
            
            app.logger.info('Health check passed')
            
            return jsonify({
                'status': 'healthy',
                'timestamp': datetime.utcnow().isoformat() + 'Z'
            }), 200
            
        except Exception as e:
            app.logger.error(f'Health check failed: {str(e)}', exc_info=True)
            return jsonify({
                'status': 'unhealthy',
                'error': str(e),
                'timestamp': datetime.utcnow().isoformat() + 'Z'
            }), 503


# Create the Flask app instance
app = create_app()


if __name__ == '__main__':
    # For local development only
    port = int(os.environ.get('PORT', 8080))
    app.run(host='0.0.0.0', port=port, debug=(Config.FLASK_ENV != 'production'))