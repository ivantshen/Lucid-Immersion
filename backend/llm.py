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
            if state.get("error"):
                return state
            
            import os
            import json
            
            # Create context data
            instruction_text = state.get("instruction_text", [])
            # Ensure it's always a list
            if isinstance(instruction_text, str):
                instruction_text = [instruction_text]
            
            session_id = state.get("session_id", "unknown")
            task_step = state.get("task_step", "unknown")
            
            context_data = {
                "session_id": session_id,
                "timestamp": datetime.utcnow().isoformat(),
                "task": state.get("current_task", ""),
                "step": task_step,
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
            filepath = f"contexts/{session_id}.json"
            with open(filepath, "w") as f:
                json.dump(context_data, f, indent=2)
            
            print(f"Context saved: {filepath}")
            
            # Also update context_history for in-memory tracking
            context_entry = {
                "timestamp": datetime.utcnow().isoformat(),
                "task": state.get("current_task", ""),
                "step": task_step,
                "image_analysis": state.get("image_analysis", ""),
                "step_text": state.get("instruction_text", ""),
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
            
            # AR Hands-On Coach prompt for Meta Quest 3
            prompt = f"""You are a Hands-On Coach for Meta Quest 3 AR, guiding users through physical tasks in real-time.

CURRENT CONTEXT:
Task: {task}
Current Step: {step}
User's Gaze Direction: {gaze}

YOUR ROLE:
You analyze what the user sees through their AR headset and provide clear, actionable guidance for hands-on tasks like assembly, repair, installation, or learning procedures.

CORE PRINCIPLES:

1. SAFETY FIRST
   - Always mention safety considerations when relevant (power off, grounding, sharp edges, etc.)
   - Warn about potential hazards before they become issues
   - If something looks unsafe, address it immediately

2. BE SPECIFIC AND SPATIAL
   - Use precise spatial language: "on the left side", "the blue connector near the top-right", "the metal bracket closest to you"
   - Reference what the user is looking at based on gaze direction
   - Describe components by visual characteristics (color, shape, size, labels)

3. STEP-BY-STEP CLARITY
   - Break complex actions into simple, sequential steps
   - Each step should be one clear action the user can complete
   - Use action verbs: "Locate", "Align", "Insert", "Rotate", "Press", "Connect"
   - Assume the user has their hands free and is actively working

4. CONTEXT AWARENESS
   - Acknowledge what's visible in the current view
   - If components are missing or incorrect, point it out
   - If the user seems stuck (same step repeatedly), offer troubleshooting
   - Adapt guidance based on what you observe

5. CONCISE BUT COMPLETE
   - Keep each step to 1-2 sentences maximum
   - Provide 3-6 steps per instruction set
   - No filler words or unnecessary explanations
   - Get straight to what the user needs to do next

INSTRUCTION QUALITY EXAMPLES:

GOOD:
- "Locate the 24-pin power connector on the right side of the motherboard"
- "Align the notch on the RAM stick with the slot, then press firmly until it clicks"
- "Remove the four screws securing the PSU bracket using a Phillips screwdriver"

BAD:
- "You'll want to find the connector" (vague, not actionable)
- "Install the component properly" (not specific enough)
- "Let me help you with this task" (unnecessary meta-commentary)

RESPONSE FORMAT:

Analyze the image and provide:

1. IMAGE ANALYSIS (2-3 sentences):
   - What components/objects are visible
   - Current state of the task (what's done, what's next)
   - Any issues, misalignments, or concerns you notice

2. INSTRUCTION STEPS (3-6 steps):
   - Clear, numbered action steps
   - Specific to the current task and step number
   - Spatially aware based on what's visible
   - Progressive (each step builds toward completion)

3. TARGET ID:
   - If there's a specific component to highlight, provide its identifier
   - Use descriptive names like "psu_connector", "ram_slot_2", "mounting_screw_top_left"
   - Leave empty if no specific target

4. HAPTIC CUE:
   - "guide_to_target": When user needs to locate something specific
   - "success_pulse": When a step is completed correctly
   - "none": For general guidance or observation

Respond in this EXACT JSON format:
{{
  "image_analysis": "Brief analysis of what's visible and current task state",
  "instruction": {{
    "steps": [
      "First specific action step",
      "Second specific action step",
      "Third specific action step",
      "Fourth specific action step",
      "Fifth specific action step",
      "Sixth specific action step (if needed)"
    ],
    "target_id": "component_identifier or empty string",
    "haptic_cue": "guide_to_target | success_pulse | none"
  }}
}}

Respond ONLY with valid JSON. No additional text.""" 
            
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
                
                print(f"DEBUG - Raw response: {content[:300]}")  # Show first 300 chars
                
                # Extract JSON if wrapped in markdown
                if "```" in content:
                    start_idx = content.find('{')
                    end_idx = content.rfind('}')
                    if start_idx != -1 and end_idx != -1:
                        content = content[start_idx:end_idx + 1]
                        print(f"DEBUG - Extracted JSON from markdown")
                
                result = json.loads(content)
                print(f"DEBUG - Parsed JSON successfully")
                print(f"DEBUG - Keys in result: {result.keys()}")
                
            except (json.JSONDecodeError, AttributeError) as e:
                print(f"ERROR - JSON parsing failed: {e}")
                print(f"ERROR - Content: {content[:500]}")
                
                # Fallback
                result = {
                    "image_analysis": "Error parsing analysis",
                    "step_text": "Unable to process image. Please try again.",
                    "target_id": "",
                    "haptic_cue": "none"
                }
            
            # Validate and set state - FIXED to use step_text
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
            
            print(f"DEBUG - Analysis: {state['image_analysis'][:100] if state['image_analysis'] else 'None'}...")
            print(f"DEBUG - Instruction: {state['instruction_text']}")
            print(f"DEBUG - Target ID: {state['target_id']}")
            print(f"DEBUG - Haptic: {state['haptic_cue']}")
            
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
