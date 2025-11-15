# Requirements Document

## Introduction

The Project Mentor Backend is a cloud-based AI reasoning system that provides real-time, context-aware procedural guidance to AR headset users. The system receives multimodal input (images, gaze data, task state) from Unity clients, processes this information through a LangGraph workflow that sends prompts to a multimodal LLM, and returns step-by-step instructions with haptic cues. The backend is built with Flask and must handle concurrent requests, maintain session context, and provide sub-second response times for an optimal user experience.

## Glossary

- **Backend System**: The cloud-hosted Flask application running on Google Cloud Run
- **Unity Client**: The Meta Quest 3 application that sends assistance requests
- **Context Package**: The multimodal data payload sent from Unity (image, task state, gaze vector)
- **LangGraph Workflow**: The stateful graph-based orchestration system managing the AI reasoning process
- **Session Context**: Persistent state data associated with a user's assistance session
- **Multimodal LLM**: Google Gemini model capable of processing both text and image inputs
- **Checkpoint Store**: LangGraph's persistence mechanism for saving workflow state

## Requirements

### Requirement 1

**User Story:** As a Unity client, I want to send a context package with an image and receive AI-generated instructions, so that I can display guidance to the AR headset user

#### Acceptance Criteria

1. WHEN the Unity Client sends a POST request to /assist with a valid context package, THE Backend System SHALL return a JSON response within 3 seconds containing instruction text, target ID, and haptic cue
2. WHEN the Unity Client sends an image larger than 2MB, THE Backend System SHALL compress the image to under 1MB before processing
3. IF the Unity Client sends a malformed context package, THEN THE Backend System SHALL return a 400 status code with a descriptive error message
4. THE Backend System SHALL accept multipart/form-data requests with fields: snapshot (file), task_step (string), current_task (string), gaze_vector (JSON string), and optional session_id (string)
5. WHEN the Backend System processes a request without a session_id, THE Backend System SHALL generate a new UUID session identifier and return it in the response

### Requirement 2

**User Story:** As a backend system, I want to analyze images using multimodal AI, so that I can understand what the user is looking at and identify relevant components

#### Acceptance Criteria

1. WHEN the LangGraph Workflow receives an image in the analyze_image node, THE Backend System SHALL invoke Google Gemini with the image and contextual prompt
2. THE Backend System SHALL extract component names, user focus area, and potential issues from the Gemini response
3. WHEN Gemini analysis completes, THE Backend System SHALL store the analysis text in the session state
4. THE Backend System SHALL include task context (current_task, task_step, gaze_vector) in the Gemini prompt to improve analysis accuracy
5. IF the Gemini API returns an error, THEN THE Backend System SHALL retry once before returning an error response

### Requirement 3

**User Story:** As a backend system, I want to generate clear, actionable instructions based on image analysis and task context, so that users receive helpful guidance

#### Acceptance Criteria

1. WHEN the LangGraph Workflow reaches the generate_instruction node, THE Backend System SHALL invoke Google Gemini with the image analysis and task context
2. THE Backend System SHALL construct a prompt that includes current_task, task_step, image analysis, and gaze_vector to provide comprehensive context
3. THE Backend System SHALL parse the Gemini response to extract instruction_text, target_id, and haptic_cue fields
4. THE Backend System SHALL limit instruction_text to 3 sentences maximum for AR display readability
5. THE Backend System SHALL validate that haptic_cue is one of: "guide_to_target", "success_pulse", or "none"

### Requirement 4

**User Story:** As a backend system, I want to persist session context across multiple requests, so that I can provide continuity in multi-step guidance

#### Acceptance Criteria

1. WHEN the LangGraph Workflow completes, THE Backend System SHALL save the workflow state to the Checkpoint Store using the session_id as the thread identifier
2. WHEN a Unity Client sends a request with an existing session_id, THE Backend System SHALL load the previous workflow state from the Checkpoint Store
3. THE Backend System SHALL store session context as JSON files in a contexts directory with filename pattern {session_id}.json
4. THE Backend System SHALL include in the JSON context: session_id, task, step, image_analysis, and instruction
5. WHEN a session has been inactive for 24 hours, THE Backend System SHALL delete the associated context files

### Requirement 5

**User Story:** As a backend system, I want to handle concurrent requests efficiently, so that multiple users can receive assistance simultaneously

#### Acceptance Criteria

1. THE Backend System SHALL use threading or async patterns with Flask for concurrent I/O operations (API calls, file operations)
2. THE Backend System SHALL support at least 10 concurrent requests without degradation in response time using gunicorn with multiple workers
3. WHEN Google Cloud Run scales to zero, THE Backend System SHALL complete cold start initialization within 5 seconds
4. THE Backend System SHALL implement request timeouts of 30 seconds to prevent hanging connections
5. THE Backend System SHALL handle LLM API rate limits gracefully with exponential backoff retry logic

### Requirement 6

**User Story:** As a system administrator, I want comprehensive logging and error handling, so that I can debug issues and monitor system health

#### Acceptance Criteria

1. THE Backend System SHALL log all incoming requests with timestamp, session_id, current_task, and task_step
2. WHEN an error occurs in any LangGraph node, THE Backend System SHALL log the error with full stack trace and state context
3. THE Backend System SHALL expose a /health endpoint that returns 200 status when the Gemini API is reachable
4. THE Backend System SHALL use structured logging with JSON format for integration with Google Cloud Logging
5. THE Backend System SHALL track and log response latency for each LangGraph node to identify bottlenecks

### Requirement 7

**User Story:** As a backend system, I want to validate and sanitize all inputs, so that I can prevent security vulnerabilities and data corruption

#### Acceptance Criteria

1. THE Backend System SHALL validate that snapshot files are valid JPEG or PNG images before processing
2. THE Backend System SHALL reject images larger than 5MB with a 413 status code
3. THE Backend System SHALL validate that gaze_vector is a valid JSON object with x, y, z numeric fields
4. THE Backend System SHALL sanitize task_step and current_task strings to prevent injection attacks
5. THE Backend System SHALL authenticate requests using an API key passed in the Authorization header

### Requirement 8

**User Story:** As a backend system, I want to optimize costs and resource usage, so that the system remains economical during the hackathon

#### Acceptance Criteria

1. THE Backend System SHALL use Google Gemini Flash model for image analysis to minimize API costs
2. WHEN Google Cloud Run scales to zero after 15 minutes of inactivity, THE Backend System SHALL not incur compute costs
3. THE Backend System SHALL compress images to 768x768 resolution before sending to Gemini to reduce API payload size
4. THE Backend System SHALL limit conversation history in LangGraph state to the last 5 messages to reduce token usage
5. THE Backend System SHALL use streaming responses from Gemini when available to improve perceived latency
