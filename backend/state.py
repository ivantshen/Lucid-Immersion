"""
State definition for the VR context workflow.
Defines the data structure that flows through the LangGraph workflow.
"""
from typing import TypedDict, List, Optional, Annotated
import operator


class VRContextState(TypedDict):
    """
    State for VR context information.
    
    This state is passed through the LangGraph workflow and updated by each node.
    All fields are optional to allow partial state updates.
    """
    
    # Input fields (provided by Unity client)
    current_image: Optional[str]  # base64 encoded image from AR headset
    task_step: Optional[str]  # Current step number (e.g., "4")
    current_task: Optional[str]  # Task identifier (e.g., "PSU_Install")
    gaze_vector: Optional[dict]  # User's gaze direction {"x": float, "y": float, "z": float}
    session_id: Optional[str]  # Session identifier for context persistence

    # Intermediate fields (generated during workflow)
    image_analysis: Optional[str]  # Analysis result from Gemini Vision

    # Output fields (returned to Unity client)
    instruction_text: Optional[str]  # Generated instruction text (max 3 sentences)
    target_id: Optional[str]  # Component ID to highlight in AR
    haptic_cue: Optional[str]  # Haptic feedback type: "guide_to_target" | "success_pulse" | "none"

    # Context history (for multi-turn conversations)
    context_history: Annotated[List[dict], operator.add]  # Accumulate context over time
    
    # Messages (for LangGraph message tracking)
    messages: Optional[List]  # LangChain messages for conversation history
    
    # Error handling
    error: Optional[str]  # Error message if something goes wrong


def create_initial_state(
    image_base64: str,
    task_step: str,
    current_task: str,
    gaze_vector: dict,
    session_id: str
) -> VRContextState:
    """
    Create an initial state for the workflow.
    
    Args:
        image_base64: Base64 encoded image from AR headset
        task_step: Current step number
        current_task: Task identifier
        gaze_vector: User's gaze direction {"x": float, "y": float, "z": float}
        session_id: Session identifier
        
    Returns:
        VRContextState ready for workflow execution
    """
    return {
        "current_image": image_base64,
        "task_step": task_step,
        "current_task": current_task,
        "gaze_vector": gaze_vector,
        "session_id": session_id,
        "context_history": [],
        "messages": [],
        "image_analysis": None,
        "instruction_text": None,
        "target_id": None,
        "haptic_cue": None,
        "error": None
    }


def extract_response(state: VRContextState) -> dict:
    """
    Extract the response fields from the final state.
    
    Args:
        state: Final workflow state after execution
        
    Returns:
        Dictionary with response fields for Unity client
    """
    return {
        "session_id": state.get("session_id", ""),
        "instruction_text": state.get("instruction_text", ""),
        "target_id": state.get("target_id", ""),
        "haptic_cue": state.get("haptic_cue", "none"),
        "error": state.get("error")
    }


# Example usage
if __name__ == "__main__":
    # Create a sample initial state
    initial_state = create_initial_state(
        image_base64="sample_base64_image_data",
        task_step="4",
        current_task="PSU_Install",
        gaze_vector={"x": 0.5, "y": -0.2, "z": 0.8},
        session_id="test-session-123"
    )
    
    print("Initial state created:")
    print(f"Task: {initial_state['current_task']}")
    print(f"Step: {initial_state['task_step']}")
    print(f"Session: {initial_state['session_id']}")
    print(f"Gaze: {initial_state['gaze_vector']}")
