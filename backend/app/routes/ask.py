"""
/ask endpoint for text-only and voice follow-up questions.
"""
import json
from datetime import datetime
from flask import current_app, request, jsonify
from werkzeug.exceptions import BadRequest, Unauthorized, NotFound, RequestEntityTooLarge
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_core.messages import HumanMessage
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

# Import utilities
from app.utils.session import load_session_context
from app.utils.validation import sanitize_string
from app.utils.audio_validation import validate_audio, MAX_AUDIO_SIZE
from app.utils.speech_to_text import transcribe_audio


def register_ask_route(app):
    """Register the /ask endpoint with the Flask app."""
    
    @app.route('/ask', methods=['POST'])
    def ask():
        """
        Follow-up question endpoint for text and voice queries about previous sessions.
        
        Accepts either:
        1. JSON with:
           - session_id: str (required, must reference existing session)
           - question: str (required, user's follow-up question)
        
        2. Multipart/form-data with:
           - session_id: str (required, must reference existing session)
           - audio: file (optional, audio file for voice input)
           - question: str (optional, text question - used if no audio provided)
        
        Returns JSON with answer based on previous session context.
        """
        start_time = datetime.utcnow()
        session_id = None
        
        try:
            # 1. Authenticate request
            auth_header = request.headers.get('Authorization', '')
            if not auth_header.startswith('Bearer '):
                raise Unauthorized('Missing or invalid Authorization header')
            
            api_key = auth_header.replace('Bearer ', '').strip()
            if api_key != app.config['API_KEY']:
                raise Unauthorized('Invalid API key')
            
            # 2. Parse request data (JSON or multipart/form-data)
            question = None
            is_voice_input = False
            
            if request.is_json:
                # Text-only request (backward compatibility)
                data = request.get_json()
                session_id = data.get('session_id')
                question = data.get('question')
                
            elif request.content_type and 'multipart/form-data' in request.content_type:
                # Multipart request (potentially with audio)
                session_id = request.form.get('session_id')
                
                # Check if audio file is provided
                if 'audio' in request.files:
                    audio_file = request.files['audio']
                    
                    # Validate audio file
                    is_valid, error_msg = validate_audio(audio_file)
                    if not is_valid:
                        if 'too large' in error_msg.lower():
                            raise RequestEntityTooLarge(error_msg)
                        else:
                            raise BadRequest(error_msg)
                    
                    # Read audio bytes
                    audio_bytes = audio_file.read()
                    audio_file.seek(0)
                    
                    # Transcribe audio to text
                    app.logger.info(f'Transcribing audio for session {session_id}')
                    success, transcribed_text, error_msg = transcribe_audio(
                        audio_bytes,
                        audio_file.content_type
                    )
                    
                    if not success:
                        raise BadRequest(f'Audio transcription failed: {error_msg}')
                    
                    question = transcribed_text
                    is_voice_input = True
                    app.logger.info(f'Audio transcribed: "{question}"')
                    
                else:
                    # No audio, check for text question
                    question = request.form.get('question')
            else:
                raise BadRequest('Request must be JSON or multipart/form-data')
            
            # 3. Validate required fields
            if not session_id:
                raise BadRequest('Missing required field: session_id')
            
            if not question:
                raise BadRequest('Missing required field: question or audio')
            
            # 4. Sanitize inputs
            session_id = sanitize_string(session_id)
            question = sanitize_string(question)
            
            # 5. Load session context
            context_dir = app.config.get('CONTEXT_DIR', 'contexts')
            session_context = load_session_context(session_id, context_dir)
            
            if session_context is None:
                raise NotFound(f'Session not found: {session_id}')
            
            # 6. Build prompt with previous context
            task = session_context.get('task', 'Unknown task')
            step = session_context.get('step', 'Unknown step')
            image_analysis = session_context.get('image_analysis', '')
            previous_instruction = session_context.get('instruction', {})
            
            # Extract instruction steps
            instruction_steps = previous_instruction.get('steps', [])
            if isinstance(instruction_steps, str):
                instruction_steps = [instruction_steps]
            instruction_text = '\n'.join(f"- {step}" for step in instruction_steps)
            
            prompt = f"""You are an expert technical assistant helping a user with: {task}

Previous Context:
- Current Step: {step}
- Image Analysis: {image_analysis}
- Previous Instruction:
{instruction_text}

The user has a follow-up question about this context:
"{question}"

Provide a clear, helpful answer as a numbered list of actionable steps. Format your response as:
1. First step or point
2. Second step or point
3. Third step or point
(etc.)

Be concise, practical, and based on the previous context."""
            
            # 7. Invoke Gemini text-only model (cheaper, no image)
            app.logger.info(f'Processing follow-up question for session {session_id}')
            
            if not app.config.get('GEMINI_API_KEY'):
                raise Exception('GEMINI_API_KEY not configured')
            
            llm = ChatGoogleGenerativeAI(
                model="gemini-2.5-flash",
                google_api_key=app.config['GEMINI_API_KEY'],
                temperature=0.5
            )
            
            message = HumanMessage(content=prompt)
            response = llm.invoke([message])
            
            answer_text = response.content
            
            # Parse the answer into steps (split by numbered list items)
            import re
            # Match patterns like "1. ", "2. ", etc.
            steps = re.split(r'\n\s*\d+\.\s+', answer_text)
            # Remove empty first element if answer starts with "1. "
            if steps and not steps[0].strip():
                steps = steps[1:]
            # Clean up each step
            answer_steps = [step.strip() for step in steps if step.strip()]
            
            # If parsing failed or no steps found, use the whole answer as a single step
            if not answer_steps:
                answer_steps = [answer_text.strip()]
            
            # 8. Save follow-up Q&A to context file
            if 'follow_up_qa' not in session_context:
                session_context['follow_up_qa'] = []
            
            session_context['follow_up_qa'].append({
                'timestamp': datetime.utcnow().isoformat() + 'Z',
                'question': question,
                'answer_steps': answer_steps
            })
            
            # Write updated context back to file
            import os
            from pathlib import Path
            context_path = Path(context_dir) / f"{session_id}.json"
            with open(context_path, 'w') as f:
                json.dump(session_context, f, indent=2)
            
            # 9. Build response
            response_data = {
                'status': 'success',
                'session_id': session_id,
                'answer_steps': answer_steps,
                'context': {
                    'task': task,
                    'step': step,
                    'previous_instruction': instruction_text
                }
            }
            
            # Add transcribed question if voice input was used
            if is_voice_input:
                response_data['transcribed_question'] = question
            
            # 10. Log completion
            duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
            app.logger.info('Follow-up question completed', extra={
                'session_id': session_id,
                'endpoint': '/ask',
                'task': task,
                'step': step,
                'duration_ms': duration_ms,
                'status': 'success'
            })
            
            return jsonify(response_data), 200
            
        except (BadRequest, Unauthorized, NotFound, RequestEntityTooLarge) as e:
            # Re-raise HTTP exceptions
            raise
            
        except Exception as e:
            # Log and return 500 for unexpected errors
            duration_ms = int((datetime.utcnow() - start_time).total_seconds() * 1000)
            app.logger.error(f'Follow-up question failed: {str(e)}', exc_info=True, extra={
                'endpoint': '/ask',
                'session_id': session_id,
                'duration_ms': duration_ms,
                'status': 'error'
            })
            
            return jsonify({
                'status': 'error',
                'error': str(e),
                'error_code': 'LLM_ERROR',
                'session_id': session_id
            }), 500
