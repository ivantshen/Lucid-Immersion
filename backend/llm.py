import base64
import os
import json
from datetime import datetime
from typing import TypedDict, List, Optional, Annotated
import operator
from langgraph.graph import StateGraph, MessagesState, START, END
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_core.messages import HumanMessage, AIMessage, SystemMessage

class VRContextState(TypedDict):
    """State for VR context information"""
    # Input fields
    current_image: Optional[str]  # base64 encoded image
    task_step: Optional[str]
    current_task: Optional[str]
    gaze_vector: Optional[dict]  # {"x": float, "y": float, "z": float}
    session_id: Optional[str]

    # Intermediate fields
    image_analysis: Optional[str]

    # Output fields
    instruction_text: Optional[List[str]]  # List of instruction steps
    target_id: Optional[str]
    haptic_cue: Optional[str]

    # Context history
    context_history: Annotated[List[dict], operator.add]  # Accumulate context over time
    
    # Messages for LangGraph
    messages: Optional[List]

class VRContextWorkflow:
    """
    LangGraph workflow for procesing VR headset images with context awareness
    """

    def __init__(self, api_key: str, max_context_history: int = 10):
        """Initialize the VR context workflow"""

        # self.llm = ChatAnthropic(
        #     model="claude-sonnet-4-5-20250929",
        #     anthropic_api_key=api_key,
        #     max_tokens=1024,
        #     temperature=0.2
        # )
        self.llm = ChatGoogleGenerativeAI(
            model="gemini-2.5-flash",
            google_api_key=api_key,
            max_tokens=4096,
            temperature=0.2,
            response_mime_type="application/json"
        )
        self.max_context_history = max_context_history
        self.workflow = self.build_workflow()

    def build_workflow(self) -> StateGraph:
        """Build the LangGraph workflow"""

        workflow = StateGraph(VRContextState)

        # Add nodes and edges to the workflow as needed
        workflow.add_node("analyze_and_instruct", self.analyze_and_instruct)
        workflow.add_node("save_context", self.save_context)

        workflow.set_entry_point("analyze_and_instruct")
        workflow.add_edge("analyze_and_instruct", "save_context")
        workflow.add_edge("save_context", END)

        return workflow.compile()

    
    def save_context(self, state: VRContextState) -> VRContextState:
        """
        Node to save the context to JSON file
        """
        try:
            import os
            import json
            
            # Create context data
            instruction_text = state.get("instruction_text", [])
            # Ensure it's always a list
            if isinstance(instruction_text, str):
                instruction_text = [instruction_text]
            
            context_data = {
                "session_id": state.get("session_id", "unknown"),
                "timestamp": datetime.utcnow().isoformat(),
                "task": state.get("current_task", ""),
                "step": state.get("task_step", ""),
                "gaze_vector": state.get("gaze_vector", {}),
                "image_analysis": state.get("image_analysis", ""),
                "instruction": {
                    "steps": instruction_text,  # Now a list
                    "target_id": state.get("target_id", ""),
                    "haptic_cue": state.get("haptic_cue", "none")
                },
                "error": state.get("error", None)
            }
        
            # Ensure contexts directory exists
            os.makedirs("contexts", exist_ok=True)
            
            # Save to file
            session_id = state.get("session_id", "unknown")
            filepath = f"contexts/{session_id}.json"
            with open(filepath, "w") as f:
                json.dump(context_data, f, indent=2)
            
            print(f"Context saved: {filepath}")
            
            # Also update context_history for in-memory tracking
            context_entry = {
                "timestamp": datetime.utcnow().isoformat(),
                "task": state.get("current_task", ""),
                "step": state.get("task_step", ""),
                "image_analysis": state.get("image_analysis", ""),
            }
            
            if "context_history" not in state:
                state["context_history"] = []
            
            state["context_history"].append(context_entry)
            
            # Trim history if needed
            if len(state["context_history"]) > self.max_context_history:
                state["context_history"] = state["context_history"][-self.max_context_history:]
            
            return state
        except Exception as e:
            state["error"] = f"Context save failed: {str(e)}"
            return state
        
    def analyze_and_instruct(self, state: VRContextState) -> VRContextState:
        """
        Combined node: Analyze image and generate instruction in one LLM call
        """
        try:
            if state.get("error"):
                return state
            
            image_data = state.get("current_image")
            if not image_data:
                state["error"] = "No image data provided"
                state["image_analysis"] = "Error: No image data"
                state["instruction_text"] = "Error: No image to analyze"
                state["target_id"] = ""
                state["haptic_cue"] = "none"
                return state
            
            task = state.get("current_task", "Unknown task")
            step = state.get("task_step", "Unknown step")
            gaze = state.get("gaze_vector", {})
            
            # Combined prompt
            prompt = f"""You are an AR assistance system analyzing a user's view.

    Task: {task}
    Current Step: {step}
    Gaze Direction: {gaze}

    First, analyze what you see in the image:
    - What objects/components are visible?
    - What is the user focused on?
    - Any issues or points of confusion?

    Then, provide step-by-step instructions for this task.

    Respond in JSON format:
    {{
    "image_analysis": "Brief analysis of the scene (2-3 sentences)",
    "instruction": {{
        "steps": [
            "First instruction step",
            "Second instruction step",
            "Third instruction step"
        ],
        "target_id": "component ID if applicable, otherwise empty string",
        "haptic_cue": "guide_to_target | success_pulse | none"
    }}
    }}
    
    Provide 2-5 clear, actionable instruction steps.
    Respond ONLY with valid JSON."""
            
            # Create multimodal message
            message = HumanMessage(
                content=[
                    {"type": "text", "text": prompt},
                    {
                        "type": "image_url",
                        "image_url": f"data:image/jpeg;base64,{image_data}"
                    }
                ]
            )
            
            print(f"DEBUG - Processing image for task: {task}, step: {step}")
            
            # Invoke with retry
            max_retries = 2
            for attempt in range(max_retries):
                try:
                    response = self.llm.invoke([message])
                    print(f"DEBUG - Response received: {len(response.content)} chars")
                    break
                except Exception as e:
                    print(f"ERROR - Attempt {attempt + 1} failed: {e}")
                    if attempt < max_retries - 1:
                        import time
                        time.sleep(2)
                    else:
                        raise
            
            # Parse JSON response
            import json
            import re
            
            try:
                content = response.content.strip()
                
                # Extract JSON if wrapped in markdown
                if "```" in content:
                    start_idx = content.find('{')
                    end_idx = content.rfind('}')
                    if start_idx != -1 and end_idx != -1:
                        content = content[start_idx:end_idx + 1]
                
                result = json.loads(content)
                print(f"DEBUG - Parsed JSON successfully")
                
            except (json.JSONDecodeError, AttributeError) as e:
                print(f"ERROR - JSON parsing failed: {e}")
                print(f"ERROR - Content: {content[:200]}")
                
                # Fallback
                result = {
                    "image_analysis": "Error parsing analysis",
                    "instruction_text": "Unable to process image. Please try again.",
                    "target_id": "",
                    "haptic_cue": "none"
                }
            
            # Validate and set state
            valid_cues = ["guide_to_target", "success_pulse", "none"]
            
            state["image_analysis"] = result.get("image_analysis", "No analysis available")
            
            # Handle nested instruction object
            instruction = result.get("instruction", {})
            if isinstance(instruction, dict):
                # Get steps as a list
                steps = instruction.get("steps", [])
                if isinstance(steps, list) and len(steps) > 0:
                    state["instruction_text"] = steps  # Store as list
                else:
                    # Fallback to text field if steps not provided
                    text = instruction.get("text", "No instruction available")
                    state["instruction_text"] = [text] if isinstance(text, str) else ["No instruction available"]
                
                state["target_id"] = instruction.get("target_id", "")
                state["haptic_cue"] = instruction.get("haptic_cue", "none")
            else:
                # Fallback for flat structure
                text = result.get("instruction_text", "No instruction available")
                state["instruction_text"] = [text] if isinstance(text, str) else ["No instruction available"]
                state["target_id"] = result.get("target_id", "")
                state["haptic_cue"] = result.get("haptic_cue", "none")
            
            if state["haptic_cue"] not in valid_cues:
                state["haptic_cue"] = "none"
            
            # Store messages
            if "messages" not in state or state["messages"] is None:
                state["messages"] = []
            state["messages"].append(message)
            state["messages"].append(response)
            
            print(f"DEBUG - Analysis: {state['image_analysis'][:100]}...")
            print(f"DEBUG - Instruction: {state['instruction_text']}")
            
            return state
            
        except Exception as e:
            error_msg = f"Analysis and instruction failed: {str(e)}"
            state["error"] = error_msg
            state["image_analysis"] = f"Error: {str(e)}"
            state["instruction_text"] = "System error. Please try again."
            state["target_id"] = ""
            state["haptic_cue"] = "none"
            print(f"ERROR - {error_msg}")
            import traceback
            traceback.print_exc()
            return state
    
    def _build_context_summary(self, context_history: List[dict]) -> str:
        """Build a summary of recent context history"""
        if not context_history:
            return "No previous context available."
        
        summary_lines = ["Recent Context History:"]
        for i, entry in enumerate(context_history[-5:], 1):  # Last 5 entries
            summary_lines.append(
                f"{i}. [{entry['timestamp']}] Scene: {entry['scene']}, "
                f"Action: {entry['action']}"
            )
        
        return "\n".join(summary_lines)
        
    def run(self, image_base64: str, task_step: str, current_task: str, 
            gaze_vector: dict, session_id: str) -> dict:
        """
        Run the workflow with a new AR assistance request.
        
        This is the main entry point for processing AR assistance requests.
        It creates the initial state, runs the workflow, and returns the result.
        
        Args:
            image_base64: Base64 encoded image from VR headset
            task_step: Current step in the task (e.g., "4")
            current_task: Name/ID of the current task (e.g., "PSU_Install")
            gaze_vector: User's gaze direction {"x": float, "y": float, "z": float}
            session_id: Session identifier for context persistence
            
        Returns:
            Final state dict with:
                - instruction_text: Generated instruction
                - target_id: Component to highlight
                - haptic_cue: Haptic feedback type
                - session_id: Session identifier
                - error: Error message if something went wrong
        """
        # Create initial state
        initial_state = {
            "current_image": image_base64,
            "task_step": task_step,
            "current_task": current_task,
            "gaze_vector": gaze_vector,
            "session_id": session_id,
            "context_history": [],
            "messages": []
        }
        
        # Run workflow
        result = self.workflow.invoke(initial_state)
        
        return result
    
# Example usage and testing
if __name__ == "__main__":
    import os
    
    # Initialize workflow
    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        print("Please set GEMINI_API_KEY environment variable")
        exit(1)
    
    workflow = VRContextWorkflow(api_key)
    
    # Example: Process a VR image (you would replace this with actual VR capture)
    # For testing, you'd need to provide a real base64 image
    print("VR Context Workflow initialized successfully!")
    print("\nWorkflow steps:")
    print("1. Capture image from VR headset")
    print("2. Analyze image with Claude Vision")
    print("3. Store context with timestamp")
    print("4. Generate context-aware LLM response")
    print("\nTo use: workflow.run(image_base64, user_query)")
