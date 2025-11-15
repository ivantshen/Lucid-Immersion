# Implementation Plan

- [x] 1. Set up project structure and dependencies
  - Create backend directory structure with app/, contexts/, and tests/ folders
  - Create requirements.txt with Flask, LangGraph, langchain-google-genai, Pillow, gunicorn, and pytest
  - Create .env.example file with required environment variables (GEMINI_API_KEY, API_KEY)
  - Create Dockerfile for containerization with Python 3.11-slim base image
  - _Requirements: 5.1, 5.3_

- [x] 2. Implement Flask application core





  - [x] 2.1 Create main Flask application with configuration


    - Write app/main.py with Flask app initialization
    - Implement Config class to load environment variables
    - Set up JSON logging with structured format
    - Add error handlers for 400, 401, 413, 500, 503 status codes
    - _Requirements: 1.3, 6.1, 6.2_

  - [x] 2.2 Implement health check endpoint


    - Create /health GET endpoint that tests Gemini API connectivity
    - Return JSON with status and timestamp
    - Handle Gemini API failures with 503 status
    - _Requirements: 6.3_

  - [x] 2.3 Write tests for Flask core


    - Write tests/test_main.py
    - Test Flask app initialization and configuration loading
    - Test error handlers return correct status codes and JSON format
    - Test /health endpoint with successful and failed Gemini connectivity
    - _Requirements: 1.3, 6.1, 6.2, 6.3_

- [ ] 3. Implement input validation utilities
  - [ ] 3.1 Create image validation function
    - Write app/utils/validation.py with validate_image function
    - Check file type (JPEG/PNG only)
    - Validate file size (max 5MB)
    - Verify image integrity using Pillow
    - _Requirements: 7.1, 7.2_

  - [ ] 3.2 Create gaze vector validation function
    - Write validate_gaze_vector function in validation.py
    - Parse JSON string and validate structure
    - Check for required x, y, z numeric fields
    - Return parsed dict or error message
    - _Requirements: 7.3_

  - [ ] 3.3 Create input sanitization function
    - Write sanitize_string function to prevent injection attacks
    - Apply to task_step and current_task inputs
    - _Requirements: 7.4_

  - [ ] 3.4 Write tests for validation utilities
    - Write tests/test_validation.py
    - Test validate_image with valid JPEG, valid PNG, invalid type, oversized file
    - Test validate_gaze_vector with valid JSON, invalid JSON, missing fields, invalid types
    - Test sanitize_string with normal input and injection attempts
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [ ] 4. Implement image processing utilities
  - [ ] 4.1 Create image compression function
    - Create app/utils/image_processing.py with compress_image function
    - Resize images to 768x768 maintaining aspect ratio using Pillow
    - Convert RGBA to RGB if needed
    - Compress to JPEG format with quality=85
    - _Requirements: 1.2, 8.3_

  - [ ] 4.2 Write tests for image processing
    - Write tests/test_image_processing.py
    - Test compress_image with various image sizes
    - Test RGBA to RGB conversion
    - Verify output is valid JPEG under target size
    - _Requirements: 1.2, 8.3_

- [ ] 5. Implement LangGraph state and workflow
  - [ ] 5.1 Define AssistanceState TypedDict
    - Create app/graph/state.py with AssistanceState definition
    - Include input fields: image_bytes, task_step, current_task, gaze_vector, session_id
    - Include intermediate field: image_analysis
    - Include output fields: instruction_text, target_id, haptic_cue
    - Include messages list with operator.add annotation
    - _Requirements: 2.3, 4.1_

  - [ ] 5.2 Create analyze_image node
    - Write app/graph/nodes.py with analyze_image_node async function
    - Encode image to base64 for Gemini API
    - Initialize ChatGoogleGenerativeAI with gemini-1.5-flash model
    - Construct prompt with task context and gaze vector
    - Create HumanMessage with text and image_url content
    - Invoke Gemini with retry logic (retry once on failure)
    - Return image_analysis and messages in state update
    - _Requirements: 2.1, 2.2, 2.4, 2.5_

  - [ ] 5.3 Write tests for analyze_image node
    - Write tests/test_nodes.py with test_analyze_image_node
    - Mock ChatGoogleGenerativeAI to return sample analysis
    - Verify image is base64 encoded correctly
    - Test retry logic on API failure
    - Verify state updates include image_analysis and messages
    - _Requirements: 2.1, 2.2, 2.4, 2.5_

  - [ ] 5.4 Create generate_instruction node
    - Write generate_instruction_node async function in nodes.py
    - Build prompt with image_analysis, task, step, and gaze context
    - Request JSON response with instruction_text, target_id, haptic_cue
    - Parse JSON response with fallback for invalid JSON
    - Validate haptic_cue is one of: guide_to_target, success_pulse, none
    - Limit instruction_text to 3 sentences
    - Return instruction fields and messages in state update
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [ ] 5.5 Write tests for generate_instruction node
    - Add test_generate_instruction_node to tests/test_nodes.py
    - Mock ChatGoogleGenerativeAI to return JSON response
    - Test JSON parsing with valid and invalid responses
    - Test haptic_cue validation
    - Verify state updates include instruction fields
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [ ] 5.6 Create save_context node
    - Write save_context_node function in nodes.py
    - Create contexts directory if it doesn't exist
    - Build context_data dict with session_id, timestamp, task, step, gaze_vector, image_analysis, instruction
    - Save to JSON file with pattern contexts/{session_id}.json
    - Log successful save operation
    - _Requirements: 4.3, 4.4_

  - [ ] 5.7 Write tests for save_context node
    - Add test_save_context_node to tests/test_nodes.py
    - Test JSON file creation with correct filename
    - Verify file contains all required fields
    - Test contexts directory creation
    - _Requirements: 4.3, 4.4_

  - [ ] 5.8 Create and compile LangGraph workflow
    - Write app/graph/workflow.py with create_assistance_graph function
    - Initialize StateGraph with AssistanceState
    - Add nodes: analyze_image, generate_instruction, save_context
    - Set entry point to analyze_image
    - Add edges: analyze_image → generate_instruction → save_context → END
    - Compile with MemorySaver checkpointer
    - _Requirements: 4.1, 4.2_

  - [ ] 5.9 Write tests for LangGraph workflow
    - Write tests/test_workflow.py
    - Test workflow compilation succeeds
    - Test workflow execution with mocked nodes
    - Verify state flows through all nodes correctly
    - Test checkpointer saves and loads state
    - _Requirements: 4.1, 4.2_

- [ ] 6. Implement /assist endpoint
  - [ ] 6.1 Create assist route handler
    - Write app/routes/assist.py with /assist POST endpoint
    - Extract multipart/form-data fields: snapshot, task_step, current_task, gaze_vector, session_id
    - Validate Authorization header for API key authentication
    - Return 401 if authentication fails
    - _Requirements: 1.1, 1.4, 7.5_

  - [ ] 6.2 Process and validate request data
    - Validate snapshot file using validate_image function
    - Return 400 or 413 for invalid images
    - Parse and validate gaze_vector JSON
    - Sanitize task_step and current_task strings
    - Generate new UUID session_id if not provided
    - _Requirements: 1.3, 1.5, 7.1, 7.2, 7.3, 7.4_

  - [ ] 6.3 Invoke LangGraph workflow
    - Read image bytes from uploaded file
    - Compress image if larger than 1MB
    - Build initial_state dict with all input fields
    - Create config dict with thread_id set to session_id
    - Invoke graph.ainvoke with initial_state and config
    - Implement 30-second timeout for workflow execution
    - _Requirements: 1.1, 1.2, 5.4_

  - [ ] 6.4 Format and return response
    - Extract instruction_text, target_id, haptic_cue from workflow result
    - Build response JSON with status, session_id, instruction_id, step_text, target_id, haptic_cue
    - Return 200 status with JSON response
    - Handle workflow errors and return appropriate error responses (500, 503, 504)
    - Log request completion with duration
    - _Requirements: 1.1, 6.1, 6.5_

  - [ ] 6.5 Write integration tests for /assist endpoint
    - Write tests/test_assist_endpoint.py
    - Test full /assist endpoint with mock Gemini responses
    - Test authentication failure returns 401
    - Test invalid image returns 400
    - Test oversized image returns 413
    - Test successful request returns proper JSON structure
    - Test session_id generation and reuse
    - Test timeout handling
    - _Requirements: 1.1, 1.3, 1.4, 1.5, 5.4, 6.5, 7.5_

- [ ] 7. Implement context cleanup utility
  - [ ] 7.1 Create cleanup function
    - Create app/utils/cleanup.py with cleanup_old_sessions function
    - Find all JSON files in contexts/ directory
    - Check file modification time against 24-hour threshold
    - Delete files older than threshold
    - Log deleted session files
    - _Requirements: 4.5_

  - [ ] 7.2 Write tests for cleanup utility
    - Write tests/test_cleanup.py
    - Create test context files with various timestamps
    - Test cleanup deletes only old files
    - Test cleanup handles missing contexts directory
    - Verify logging of deleted files
    - _Requirements: 4.5_

- [ ] 8. Create deployment configuration
  - [ ] 8.1 Write Dockerfile
    - Use python:3.11-slim as base image
    - Copy requirements.txt and install dependencies
    - Copy app/ directory
    - Create contexts/ directory
    - Expose port 8080
    - Set CMD to run gunicorn with 4 workers and 30-second timeout
    - _Requirements: 5.2, 5.3, 5.4_

  - [ ] 8.2 Create .dockerignore file
    - Exclude __pycache__, *.pyc, .env, tests/, contexts/
    - _Requirements: 5.3_

  - [ ] 8.3 Document deployment steps
    - Create deployment instructions in backend/DEPLOY.md
    - Include steps for building Docker image
    - Include steps for pushing to Google Container Registry
    - Include steps for deploying to Cloud Run with environment variables
    - Document Secret Manager setup for API keys
    - _Requirements: 5.3_
