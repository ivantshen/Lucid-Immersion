"""
/assist endpoint for processing AR assistance requests.
"""
import base64
import json
import uuid
from datetime import datetime
from flask import current_app, request, jsonify
from werkzeug.exceptions import BadRequest, Unauthorized, RequestEntityTooLarge
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

# Import utilities
from app.utils.validation import validate_image, validate_gaze_vector, sanitize_string
from app.utils.image_processing import compress_image


def register_assist_route(app):
    """Register the /assist endpoint with the Flask app."""
    
    @app.route('/assist', methods=['POST'])
    def assist():
        """
        Main assistance endpoint that processes AR context and returns instructions.
        
        Accepts multipart/form-data with:
        - snapshot: File (JPEG/PNG image)
        - task_step: str (current step number)
        - current_task: str (task identifier)
        - gaze_vector: str (JSON with x, y, z coordinates)
        - session_id: str (optional, generated if not provided)
        
        Returns JSON with instruction, target_id, and haptic_cue.
        """
        start_time = datetime.utcnow()
        
        try:
            # 1. Authenticate request
            auth_header = request.headers.get('Authorization', '')
            if not auth_header.startswith('Bearer '):
                raise Unauthorized('Missing or invalid Authorization header')
            
            api_key = auth_header.replace('Bearer ', '').strip()
            if api_key != app.config['API_KEY']:
                raise Unauthorized('Invalid API key')
            
            # 2. Extract form data
            if 'snapshot' not in request.files:
                raise BadRequest('Missing snapshot file')
            
            snapshot_file = request.files['snapshot']
            task_step = request.form.get('task_step')
            current_task = request.form.get('current_task')
            gaze_vector_str = request.form.get('gaze_vector')
            session_id = request.form.get('session_id')
            
            if not task_step or not current_task or not gaze_vector_str:
                raise BadRequest('Missing required fields: task_step, current_task, or gaze_vector')
            
            # 3. Validate image
            is_valid, error_msg = validate_image(snapshot_file)
            if not is_valid:
                if 'too large' in error_msg.lower():
                    raise RequestEntityTooLarge(error_msg)
                raise BadRequest(error_msg)
            
            # 4. Validate and parse gaze vector
            is_valid, gaze_vector, error_msg = validate_gaze_vector(gaze_vector_str)
            if not is_valid:
                raise BadRequest(f'Invalid gaze_vector: {error_msg}')
            
            # 5. Sanitize string inputs
            task_step = sanitize_string(task_step)
            current_task = sanitize_string(current_task)
            
            # 6. Generate session_id if not provided
            if not session_id:
                session_id = str(uuid.uuid4())
            else:
                session_id = sanitize_string(session_id)
            
            # 7. Read and process image
            snapshot_file.seek(0)
            image_bytes = snapshot_file.read()
            
            # Compress if larger than 1MB
            if len(image_bytes) > 1 * 1024 * 1024:
                app.logger.info(f'Compressing image from {len(image_bytes)} bytes')
                image_bytes = compress_image(image_bytes)
                app.logger.info(f'Compressed to {len(image_bytes)} bytes')
            
            # Convert to base64
            image_base64 = base64.b64encode(image_bytes).decode('utf-8')
            
            # 8. Check if workflow is initialized
            if not hasattr(app, 'workflow') or app.workflow is None:
                raise Exception('VRContextWorkflow not initialized')
            
            # 9. Invoke LangGraph workflow
            app.logger.info(f'Processing request for session {session_id}, task {current_task}, step {task_step}')
            
            result = app.workflow.run(
                image_base64=image_base64,
                task_step=task_step,
                current_task=current_task,
                gaze_vector=gaze_vector,
                session_id=session_id
            )
            
            # 10. Check for errors in result
            if result.get('error'):
                raise Exception(result['error'])
            
            # 11. Build response
            instruction_text = result.get('instruction_text', [])
            # Ensure it's always a list for consistency
            if isinstance(instruction_text, str):
                instruction_text = [instruction_text]
            
            response_data = {
                'status': 'success',
                'session_id': session_id,
                'instruction_id': f"{session_id}-{task_step}",
                'instruction_steps': instruction_text,  # Now a list of steps
                'target_id': result.get('target_id', ''),
                'haptic_cue': result.get('haptic_cue', 'none')
            }
            
            # 12. Log completion
            duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
            app.logger.info('Request completed', extra={
                'session_id': session_id,
                'endpoint': '/assist',
                'task': current_task,
                'step': task_step,
                'duration_ms': duration_ms,
                'status': 'success'
            })
            
            return jsonify(response_data), 200
            
        except (BadRequest, Unauthorized, RequestEntityTooLarge) as e:
            # Re-raise HTTP exceptions
            raise
            
        except Exception as e:
            # Log and return 500 for unexpected errors
            duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
            app.logger.error(f'Request failed: {str(e)}', exc_info=True, extra={
                'endpoint': '/assist',
                'duration_ms': duration_ms,
                'status': 'error'
            })
            
            return jsonify({
                'status': 'error',
                'error': str(e),
                'error_code': 'LLM_ERROR',
                'session_id': session_id if 'session_id' in locals() else None
            }), 500
