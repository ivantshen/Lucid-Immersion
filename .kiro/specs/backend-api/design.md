# Design Document

## Overview

The Project Mentor Backend is a Flask-based REST API that leverages LangGraph to orchestrate a stateful AI workflow. The system receives multimodal context from Unity clients (AR passthrough images, gaze data, task state), processes this through a graph-based reasoning pipeline powered by Google Gemini, and returns actionable instructions with haptic feedback cues. The architecture prioritizes simplicity, cost-effectiveness, and rapid development for the hackathon timeline.

## Architecture

### High-Level Architecture

```
┌─────────────────┐
│  Unity Client   │
│  (Meta Quest 3) │
└────────┬────────┘
         │ HTTPS POST /assist
         │ (multipart/form-data)
         ▼
┌─────────────────────────────────────┐
│   Google Cloud Run Container        │
│                                     │
│  ┌──────────────────────────────┐  │
│  │      Flask Application       │  │
│  │                              │  │
│  │  ┌────────────────────────┐ │  │
│  │  │  /assist Endpoint      │ │  │
│  │  │  - Validate input      │ │  │
│  │  │  - Invoke LangGraph    │ │  │
│  │  └───────────┬────────────┘ │  │
│  │              │                │  │
│  │  ┌───────────▼────────────┐ │  │
│  │  │   LangGraph Workflow   │ │  │
│  │  │                        │ │  │
│  │  │  1. Analyze Image      │ │  │
│  │  │  2. Generate Instr.    │ │  │
│  │  │  3. Save Context       │ │  │
│  │  └───────────┬────────────┘ │  │
│  │              │                │  │
│  │  ┌───────────▼────────────┐ │  │
│  │  │  Checkpoint Store      │ │  │
│  │  │  (MemorySaver)         │ │  │
│  │  └────────────────────────┘ │  │
│  └──────────────────────────────┘  │
└──────────────┬──────────────────────┘
               │
               ▼
      ┌────────────────┐
      │  Google Gemini │
      │  1.5 Flash API │
      └────────────────┘
```

### Technology Stack

**Core Framework:**
- Flask 3.0+ (lightweight, simple routing)
- gunicorn (WSGI server with worker processes)

**AI Orchestration:**
- LangGraph 0.2+ (stateful workflow management)
- langchain-google-genai (Gemini integration)
- langchain-core (base abstractions)

**Image Processing:**
- Pillow (PIL) for image validation and compression
- base64 encoding for Gemini API

**Deployment:**
- Docker (containerization)
- Google Cloud Run (serverless container hosting)
- Google Secret Manager (API key storage)

**Monitoring:**
- Python logging with JSON formatter
- Google Cloud Logging (automatic integration)

## Components and Interfaces

### 1. Flask Application (`app/main.py`)

**Responsibilities:**
- Initialize Flask app and configure routes
- Load environment variables and secrets
- Set up logging and error handlers
- Initialize LangGraph workflow

**Key Routes:**
- `POST /assist` - Main assistance endpoint
- `GET /health` - Health check endpoint

**Configuration:**
```python
class Config:
    GEMINI_API_KEY: str  # From Secret Manager
    API_KEY: str  # For client authentication
    MAX_IMAGE_SIZE: int = 5 * 1024 * 1024  # 5MB
    IMAGE_COMPRESSION_SIZE: tuple = (768, 768)
    SESSION_TIMEOUT_HOURS: int = 24
    CONTEXT_DIR: str = "contexts"
```

### 2. Assistance Endpoint (`app/routes/assist.py`)

**Request Handler:**
```python
@app.route('/assist', methods=['POST'])
def assist():
    """
    Accepts multipart/form-data with:
    - snapshot: File (JPEG/PNG)
    - task_step: str
    - current_task: str
    - gaze_vector: str (JSON)
    - session_id: str (optional)
    
    Returns JSON:
    {
        "status": "success",
        "session_id": "uuid",
        "instruction_id": "uuid-step",
        "step_text": "...",
        "target_id": "...",
        "haptic_cue": "..."
    }
    """
```

**Processing Flow:**
1. Authenticate request (check API key in Authorization header)
2. Validate and parse form data
3. Validate image (type, size)
4. Compress image if needed
5. Generate or retrieve session_id
6. Invoke LangGraph workflow
7. Return formatted response

**Error Handling:**
- 400: Malformed request, invalid data
- 401: Missing or invalid API key
- 413: Image too large
- 500: Internal server error (LLM failure, etc.)
- 503: Service unavailable (Gemini API down)

### 3. LangGraph Workflow (`app/graph/workflow.py`)

**State Definition:**
```python
class AssistanceState(TypedDict):
    # Input fields
    image_bytes: bytes
    task_step: str
    current_task: str
    gaze_vector: dict  # {"x": float, "y": float, "z": float}
    session_id: str
    
    # Intermediate fields
    image_analysis: str
    
    # Output fields
    instruction_text: str
    target_id: str
    haptic_cue: str
    
    # Conversation history
    messages: Annotated[Sequence[BaseMessage], operator.add]
```

**Graph Structure:**
```
START
  │
  ▼
┌─────────────────┐
│ analyze_image   │  ← Gemini Vision analyzes AR image
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ generate_instr  │  ← Gemini generates instructions
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ save_context    │  ← Persist to JSON file
└────────┬────────┘
         │
         ▼
        END
```

**Workflow Initialization:**
```python
def create_assistance_graph():
    workflow = StateGraph(AssistanceState)
    
    # Add nodes
    workflow.add_node("analyze_image", analyze_image_node)
    workflow.add_node("generate_instruction", generate_instruction_node)
    workflow.add_node("save_context", save_context_node)
    
    # Define edges
    workflow.set_entry_point("analyze_image")
    workflow.add_edge("analyze_image", "generate_instruction")
    workflow.add_edge("generate_instruction", "save_context")
    workflow.add_edge("save_context", END)
    
    # Add checkpointing
    memory = MemorySaver()
    return workflow.compile(checkpointer=memory)
```

### 4. LangGraph Nodes (`app/graph/nodes.py`)

#### Node 1: Analyze Image

**Purpose:** Use Gemini Vision to understand what the user is looking at

**Implementation:**
```python
async def analyze_image_node(state: AssistanceState) -> dict:
    """
    Analyzes the AR passthrough image to identify:
    - Visible components
    - User's focus area (based on gaze)
    - Potential issues or confusion points
    """
    
    # Encode image for Gemini
    image_b64 = base64.b64encode(state["image_bytes"]).decode()
    
    # Initialize Gemini
    llm = ChatGoogleGenerativeAI(
        model="gemini-1.5-flash",
        temperature=0.3
    )
    
    # Construct prompt
    prompt = f"""You are analyzing an AR passthrough image from a technician.

Task: {state['current_task']}
Current Step: {state['task_step']}
Gaze Direction: {state['gaze_vector']}

Analyze the image and identify:
1. What components or objects are visible
2. What the user appears to be focused on (based on gaze)
3. Any potential issues or points of confusion

Be concise and technical. Focus on actionable observations."""
    
    # Create multimodal message
    message = HumanMessage(
        content=[
            {"type": "text", "text": prompt},
            {
                "type": "image_url",
                "image_url": f"data:image/jpeg;base64,{image_b64}"
            }
        ]
    )
    
    # Invoke with retry logic
    try:
        response = await llm.ainvoke([message])
    except Exception as e:
        # Retry once
        logger.warning(f"Gemini API error, retrying: {e}")
        await asyncio.sleep(1)
        response = await llm.ainvoke([message])
    
    return {
        "image_analysis": response.content,
        "messages": [message, response]
    }
```

#### Node 2: Generate Instruction

**Purpose:** Generate clear, actionable next steps based on image analysis

**Implementation:**
```python
async def generate_instruction_node(state: AssistanceState) -> dict:
    """
    Generates step-by-step instructions with:
    - Clear action text (max 3 sentences)
    - Target component ID (if applicable)
    - Haptic feedback cue
    """
    
    llm = ChatGoogleGenerativeAI(
        model="gemini-1.5-flash",
        temperature=0.5
    )
    
    # Build prompt with full context
    prompt = f"""You are an expert technical instructor guiding a technician through: {state['current_task']}

Current Step: {state['task_step']}
Image Analysis: {state['image_analysis']}
Gaze: {state['gaze_vector']}

Based on this context, provide the next instruction.

Requirements:
- Maximum 3 sentences
- Be specific and actionable
- If there's a specific component to interact with, provide its ID
- Suggest appropriate haptic feedback

Respond in JSON format:
{{
  "instruction_text": "Clear, concise instruction here",
  "target_id": "component_id or empty string",
  "haptic_cue": "guide_to_target | success_pulse | none"
}}"""
    
    message = HumanMessage(content=prompt)
    response = await llm.ainvoke([message])
    
    # Parse JSON response
    try:
        result = json.loads(response.content)
    except json.JSONDecodeError:
        # Fallback if LLM doesn't return valid JSON
        result = {
            "instruction_text": response.content[:200],
            "target_id": "",
            "haptic_cue": "none"
        }
    
    # Validate haptic_cue
    valid_cues = ["guide_to_target", "success_pulse", "none"]
    if result.get("haptic_cue") not in valid_cues:
        result["haptic_cue"] = "none"
    
    return {
        "instruction_text": result["instruction_text"],
        "target_id": result.get("target_id", ""),
        "haptic_cue": result["haptic_cue"],
        "messages": [message, response]
    }
```

#### Node 3: Save Context

**Purpose:** Persist session state to JSON for debugging and continuity

**Implementation:**
```python
def save_context_node(state: AssistanceState) -> dict:
    """
    Saves session context to JSON file for:
    - Debugging and analysis
    - Session continuity
    - Audit trail
    """
    
    context_data = {
        "session_id": state["session_id"],
        "timestamp": datetime.utcnow().isoformat(),
        "task": state["current_task"],
        "step": state["task_step"],
        "gaze_vector": state["gaze_vector"],
        "image_analysis": state["image_analysis"],
        "instruction": {
            "text": state["instruction_text"],
            "target_id": state["target_id"],
            "haptic_cue": state["haptic_cue"]
        }
    }
    
    # Ensure contexts directory exists
    os.makedirs("contexts", exist_ok=True)
    
    # Save to file
    filepath = f"contexts/{state['session_id']}.json"
    with open(filepath, "w") as f:
        json.dump(context_data, f, indent=2)
    
    logger.info(f"Context saved: {filepath}")
    
    return {}  # No state updates needed
```

### 5. Input Validation (`app/utils/validation.py`)

**Image Validation:**
```python
def validate_image(file) -> tuple[bool, str]:
    """
    Validates uploaded image file.
    Returns: (is_valid, error_message)
    """
    # Check file type
    if not file.content_type in ['image/jpeg', 'image/png']:
        return False, "Invalid image type. Must be JPEG or PNG"
    
    # Check file size
    file.seek(0, os.SEEK_END)
    size = file.tell()
    file.seek(0)
    
    if size > 5 * 1024 * 1024:  # 5MB
        return False, "Image too large. Maximum 5MB"
    
    # Validate it's actually an image
    try:
        img = Image.open(file)
        img.verify()
        file.seek(0)
        return True, ""
    except Exception as e:
        return False, f"Invalid image file: {str(e)}"
```

**Gaze Vector Validation:**
```python
def validate_gaze_vector(gaze_str: str) -> tuple[bool, dict, str]:
    """
    Validates and parses gaze vector JSON.
    Returns: (is_valid, parsed_dict, error_message)
    """
    try:
        gaze = json.loads(gaze_str)
        
        # Check required fields
        if not all(k in gaze for k in ['x', 'y', 'z']):
            return False, {}, "Gaze vector must have x, y, z fields"
        
        # Check types
        if not all(isinstance(gaze[k], (int, float)) for k in ['x', 'y', 'z']):
            return False, {}, "Gaze vector values must be numeric"
        
        return True, gaze, ""
    except json.JSONDecodeError as e:
        return False, {}, f"Invalid JSON: {str(e)}"
```

### 6. Image Processing (`app/utils/image_processing.py`)

**Image Compression:**
```python
def compress_image(image_bytes: bytes, target_size: tuple = (768, 768)) -> bytes:
    """
    Compresses image to target size while maintaining aspect ratio.
    Returns: compressed image bytes (JPEG)
    """
    img = Image.open(io.BytesIO(image_bytes))
    
    # Convert RGBA to RGB if needed
    if img.mode == 'RGBA':
        img = img.convert('RGB')
    
    # Resize maintaining aspect ratio
    img.thumbnail(target_size, Image.Resampling.LANCZOS)
    
    # Compress to JPEG
    output = io.BytesIO()
    img.save(output, format='JPEG', quality=85, optimize=True)
    
    return output.getvalue()
```

### 7. Context Cleanup (`app/utils/cleanup.py`)

**Session Cleanup:**
```python
def cleanup_old_sessions(max_age_hours: int = 24):
    """
    Deletes session context files older than max_age_hours.
    Should be run periodically (e.g., via Cloud Scheduler).
    """
    contexts_dir = Path("contexts")
    if not contexts_dir.exists():
        return
    
    cutoff_time = datetime.utcnow() - timedelta(hours=max_age_hours)
    
    for filepath in contexts_dir.glob("*.json"):
        # Check file modification time
        mtime = datetime.fromtimestamp(filepath.stat().st_mtime)
        
        if mtime < cutoff_time:
            filepath.unlink()
            logger.info(f"Deleted old session: {filepath.name}")
```

## Data Models

### Request Model (Multipart Form)

```python
# Received as multipart/form-data
{
    "snapshot": File,  # JPEG/PNG image
    "task_step": "4",
    "current_task": "PSU_Install",
    "gaze_vector": '{"x": 0.5, "y": -0.2, "z": 0.8}',
    "session_id": "optional-uuid"  # Optional
}
```

### Response Model (JSON)

```python
{
    "status": "success" | "error",
    "session_id": "uuid-string",
    "instruction_id": "uuid-step",
    "step_text": "Locate the 8-pin PDU cable...",
    "target_id": "J_PWR_1",
    "haptic_cue": "guide_to_target",
    "error": "error message if status=error"  # Optional
}
```

### Context Storage Model (JSON File)

```python
{
    "session_id": "uuid-string",
    "timestamp": "2025-11-15T10:30:00Z",
    "task": "PSU_Install",
    "step": "4",
    "gaze_vector": {"x": 0.5, "y": -0.2, "z": 0.8},
    "image_analysis": "The image shows a server chassis...",
    "instruction": {
        "text": "Locate the 8-pin PDU cable...",
        "target_id": "J_PWR_1",
        "haptic_cue": "guide_to_target"
    }
}
```

## Error Handling

### Error Response Format

```python
{
    "status": "error",
    "error": "Descriptive error message",
    "error_code": "INVALID_IMAGE | AUTH_FAILED | LLM_ERROR | TIMEOUT",
    "session_id": "uuid-if-available"
}
```

### Error Scenarios

1. **Invalid Image**
   - Status: 400
   - Code: INVALID_IMAGE
   - Action: Return error, log details

2. **Authentication Failure**
   - Status: 401
   - Code: AUTH_FAILED
   - Action: Return error immediately

3. **Image Too Large**
   - Status: 413
   - Code: IMAGE_TOO_LARGE
   - Action: Return error with size limit

4. **LLM API Failure**
   - Status: 500
   - Code: LLM_ERROR
   - Action: Retry once, then return error

5. **Timeout**
   - Status: 504
   - Code: TIMEOUT
   - Action: Cancel request, return error

### Logging Strategy

```python
# Structured logging format
{
    "timestamp": "2025-11-15T10:30:00Z",
    "level": "INFO",
    "session_id": "uuid",
    "endpoint": "/assist",
    "task": "PSU_Install",
    "step": "4",
    "duration_ms": 1250,
    "status": "success",
    "message": "Request completed"
}
```

## Testing Strategy

### Unit Tests

**Test Coverage:**
- Input validation functions
- Image compression logic
- JSON parsing and serialization
- Error handling paths

**Framework:** pytest

**Example:**
```python
def test_validate_image_valid_jpeg():
    with open('test_image.jpg', 'rb') as f:
        is_valid, error = validate_image(f)
    assert is_valid
    assert error == ""

def test_validate_gaze_vector_invalid_json():
    is_valid, gaze, error = validate_gaze_vector("not json")
    assert not is_valid
    assert "Invalid JSON" in error
```

### Integration Tests

**Test Scenarios:**
- Full /assist endpoint flow with mock Gemini
- LangGraph workflow execution
- Session persistence and retrieval
- Error handling for various failure modes

**Tools:** pytest with mocking

### Manual Testing

**Test Cases:**
1. Send valid request from Unity → verify response format
2. Send request with large image → verify compression
3. Send request with existing session_id → verify context loading
4. Send malformed request → verify error handling
5. Test concurrent requests → verify no race conditions

## Deployment Architecture

### Docker Container

**Dockerfile:**
```dockerfile
FROM python:3.11-slim

WORKDIR /app

# Install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application
COPY app/ ./app/

# Create contexts directory
RUN mkdir -p contexts

# Expose port
EXPOSE 8080

# Run with gunicorn
CMD ["gunicorn", "--bind", "0.0.0.0:8080", "--workers", "4", "--timeout", "30", "app.main:app"]
```

### Google Cloud Run Configuration

```yaml
service: project-mentor-backend
region: us-central1
platform: managed

container:
  image: gcr.io/project-id/backend:latest
  port: 8080

resources:
  cpu: 2
  memory: 2Gi

scaling:
  minInstances: 0
  maxInstances: 10
  concurrency: 10

environment:
  - name: FLASK_ENV
    value: production

secrets:
  - name: GEMINI_API_KEY
    version: latest
  - name: API_KEY
    version: latest
```

### Environment Variables

```bash
# Required
GEMINI_API_KEY=<from Secret Manager>
API_KEY=<for client authentication>

# Optional
FLASK_ENV=production
LOG_LEVEL=INFO
MAX_IMAGE_SIZE=5242880
IMAGE_COMPRESSION_SIZE=768,768
SESSION_TIMEOUT_HOURS=24
```

## Performance Considerations

### Latency Targets

- Cold start: < 5 seconds
- Warm request: < 3 seconds
- Image analysis: < 1.5 seconds
- Instruction generation: < 1 second

### Optimization Strategies

1. **Image Compression:** Reduce to 768x768 before sending to Gemini
2. **Async Operations:** Use async/await for all I/O
3. **Connection Pooling:** Reuse HTTP connections to Gemini
4. **Minimal Dependencies:** Keep container size small for fast cold starts
5. **Worker Processes:** Use gunicorn with 4 workers for concurrency

### Cost Optimization

1. **Scale to Zero:** Cloud Run scales to 0 when idle
2. **Gemini Flash:** Use cheaper Flash model instead of Pro
3. **Image Compression:** Reduce API payload size
4. **Request Timeouts:** Prevent hanging connections
5. **Conversation History Limit:** Keep only last 5 messages in state

## Security Considerations

### Authentication

- API key in Authorization header: `Authorization: Bearer <api_key>`
- Validate on every request
- Store securely in Secret Manager

### Input Sanitization

- Validate all file uploads
- Sanitize string inputs to prevent injection
- Limit file sizes
- Validate JSON structure

### Data Privacy

- Don't log sensitive image data
- Auto-delete session contexts after 24 hours
- Use HTTPS for all communication
- Don't persist images to disk

## Monitoring and Observability

### Health Check Endpoint

```python
@app.route('/health', methods=['GET'])
def health():
    """
    Health check endpoint for Cloud Run.
    Verifies Gemini API connectivity.
    """
    try:
        # Quick Gemini API check
        llm = ChatGoogleGenerativeAI(model="gemini-1.5-flash")
        llm.invoke([HumanMessage(content="test")])
        
        return jsonify({
            "status": "healthy",
            "timestamp": datetime.utcnow().isoformat()
        }), 200
    except Exception as e:
        return jsonify({
            "status": "unhealthy",
            "error": str(e)
        }), 503
```

### Metrics to Track

- Request count by endpoint
- Response latency (p50, p95, p99)
- Error rate by error type
- LangGraph node execution time
- Gemini API latency
- Session count and duration

### Logging

- All requests with session_id, task, step
- All errors with full stack trace
- LangGraph node transitions
- Performance metrics per request
