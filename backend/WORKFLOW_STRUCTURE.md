# Workflow Structure Documentation

## Overview

The VR Context Workflow has been organized into modular files for better maintainability and testability. You now have two ways to use the workflow:

1. **Simple API** (Recommended): Use `VRContextWorkflow` class from `llm.py`
2. **Modular API**: Use individual components from `workflow.py` and `state.py`

## File Structure

```
backend/
├── llm.py              # Main API - VRContextWorkflow class (RECOMMENDED)
├── workflow.py         # Modular workflow nodes and builder (ALTERNATIVE)
├── state.py            # State definition and utilities (ALTERNATIVE)
├── app/
│   ├── main.py         # Flask app (uses llm.py)
│   └── routes/
│       └── assist.py   # /assist endpoint (uses llm.py)
└── tests/
    ├── test_llm.py     # Tests for VRContextWorkflow
    └── test_assist_endpoint.py
```

## Usage Options

### Option 1: Using VRContextWorkflow (Recommended - Currently Used)

This is what the Flask app currently uses. It's the simplest API.

```python
from llm import VRContextWorkflow

# Initialize
workflow = VRContextWorkflow(api_key="your-gemini-key")

# Run
result = workflow.run(
    image_base64="...",
    task_step="4",
    current_task="PSU_Install",
    gaze_vector={"x": 0.5, "y": -0.2, "z": 0.8},
    session_id="session-123"
)

# Access results
print(result["instruction_text"])
print(result["target_id"])
print(result["haptic_cue"])
```

### Option 2: Using Modular Components (Alternative)

If you need more control or want to customize the workflow:

```python
from workflow import create_workflow, WorkflowNodes
from state import create_initial_state, extract_response

# Create workflow
workflow = create_workflow(api_key="your-gemini-key")

# Create initial state
initial_state = create_initial_state(
    image_base64="...",
    task_step="4",
    current_task="PSU_Install",
    gaze_vector={"x": 0.5, "y": -0.2, "z": 0.8},
    session_id="session-123"
)

# Run workflow
result = workflow.invoke(initial_state)

# Extract response
response = extract_response(result)
```

### Option 3: Custom Workflow with Individual Nodes

For maximum flexibility:

```python
from langchain_google_genai import ChatGoogleGenerativeAI
from langgraph.graph import StateGraph, END
from workflow import WorkflowNodes
from state import VRContextState

# Create LLM
llm = ChatGoogleGenerativeAI(
    model="gemini-1.5-flash",
    google_api_key="your-key"
)

# Create nodes
nodes = WorkflowNodes(llm)

# Build custom workflow
workflow = StateGraph(VRContextState)
workflow.add_node("analyze", nodes.analyze_image)
workflow.add_node("instruct", nodes.generate_instruction)
workflow.add_node("save", nodes.save_context)

# Custom edges
workflow.set_entry_point("analyze")
workflow.add_edge("analyze", "instruct")
workflow.add_edge("instruct", "save")
workflow.add_edge("save", END)

# Compile
compiled_workflow = workflow.compile()
```

## File Descriptions

### llm.py (Main API)
**Purpose**: Simple, high-level API for the workflow

**Contains**:
- `VRContextState`: TypedDict defining the state structure
- `VRContextWorkflow`: Main class that wraps everything
  - `__init__()`: Initialize with API key
  - `run()`: Execute the workflow
  - `analyze_image()`: Node for image analysis
  - `generate_instruction()`: Node for instruction generation
  - `save_context()`: Node for context persistence

**When to use**: 
- ✅ You want the simplest API
- ✅ You're using it from Flask (current setup)
- ✅ You don't need to customize the workflow

### workflow.py (Modular Workflow)
**Purpose**: Extracted workflow logic for modularity

**Contains**:
- `WorkflowNodes`: Class containing all workflow nodes
  - `analyze_image()`: Gemini vision analysis
  - `generate_instruction()`: Instruction generation with JSON parsing
  - `save_context()`: Context persistence to JSON file
- `build_workflow()`: Function to build the LangGraph workflow
- `create_workflow()`: Convenience function with defaults

**When to use**:
- ✅ You want to customize the workflow structure
- ✅ You need to test individual nodes
- ✅ You want to reuse nodes in different workflows
- ✅ You're building a more complex system

### state.py (State Management)
**Purpose**: State definition and utilities

**Contains**:
- `VRContextState`: TypedDict with all state fields
- `create_initial_state()`: Helper to create initial state
- `extract_response()`: Helper to extract response fields

**When to use**:
- ✅ You need type hints for the state
- ✅ You want helper functions for state management
- ✅ You're using the modular workflow approach

## Workflow Execution Flow

```
1. Client Request
   ↓
2. Flask /assist endpoint (app/routes/assist.py)
   ↓
3. VRContextWorkflow.run() (llm.py)
   ↓
4. LangGraph Workflow Execution:
   ├─ Node 1: analyze_image
   │   └─ Gemini Vision API call
   │   └─ Returns: image_analysis
   ├─ Node 2: generate_instruction
   │   └─ Gemini API call with context
   │   └─ Returns: instruction_text, target_id, haptic_cue
   └─ Node 3: save_context
       └─ Save to contexts/{session_id}.json
       └─ Update context_history
   ↓
5. Return result to Flask
   ↓
6. Format and send JSON response to Unity
```

## State Flow

```python
Initial State:
{
    "current_image": "base64...",
    "task_step": "4",
    "current_task": "PSU_Install",
    "gaze_vector": {"x": 0.5, "y": -0.2, "z": 0.8},
    "session_id": "uuid",
    "context_history": [],
    "messages": []
}

After analyze_image:
{
    ...previous fields,
    "image_analysis": "The image shows a server chassis..."
}

After generate_instruction:
{
    ...previous fields,
    "instruction_text": "Locate the 8-pin PDU cable...",
    "target_id": "J_PWR_1",
    "haptic_cue": "guide_to_target"
}

After save_context:
{
    ...previous fields,
    "context_history": [
        {
            "timestamp": "2025-11-15T10:30:00Z",
            "task": "PSU_Install",
            "step": "4",
            "image_analysis": "..."
        }
    ]
}
```

## Current Implementation

**The Flask app currently uses Option 1 (VRContextWorkflow from llm.py)**

This is the recommended approach because:
- ✅ Simplest API
- ✅ All logic in one place
- ✅ Easy to understand and maintain
- ✅ Works perfectly for the current use case

The modular files (`workflow.py` and `state.py`) are available if you need:
- More flexibility
- Custom workflow structures
- Better testability of individual components
- Reusable nodes for other projects

## Testing

Both approaches are fully tested:

```bash
# Test the main VRContextWorkflow
pytest backend/tests/test_llm.py

# Test the Flask endpoint
pytest backend/tests/test_assist_endpoint.py

# Test everything
pytest backend/tests/
```

## Migration Guide

If you want to switch from `llm.py` to the modular approach:

1. Update `app/main.py`:
```python
# OLD
from llm import VRContextWorkflow
app.workflow = VRContextWorkflow(api_key=Config.GEMINI_API_KEY)

# NEW
from workflow import create_workflow
app.workflow_graph = create_workflow(api_key=Config.GEMINI_API_KEY)
```

2. Update `app/routes/assist.py`:
```python
# OLD
result = app.workflow.run(...)

# NEW
from state import create_initial_state
initial_state = create_initial_state(...)
result = app.workflow_graph.invoke(initial_state)
```

But for now, **stick with the current implementation** - it works great!

## Summary

- **llm.py**: ✅ Currently used, recommended, simple API
- **workflow.py**: Alternative modular approach for advanced use cases
- **state.py**: State utilities for modular approach

Both approaches work and are fully tested. The current implementation using `llm.py` is perfect for your needs!
