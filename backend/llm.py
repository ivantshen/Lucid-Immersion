import base64
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
    instruction_text: Optional[str]
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
            max_tokens=1024,
            temperature=0.2
        )
        self.max_context_history = max_context_history
        self.workflow = self.build_workflow()

    def build_workflow(self) -> StateGraph:
        """Build the LangGraph workflow"""

        workflow = StateGraph(VRContextState)

        # Add nodes and edges to the workflow as needed
        workflow.add_node("analyze_image", self.analyze_image)
        workflow.add_node("generate_instruction", self.generate_instruction)
        workflow.add_node("save_context", self.save_context)

        workflow.set_entry_point("analyze_image")
        workflow.add_edge("analyze_image", "generate_instruction")
        workflow.add_edge("generate_instruction", "save_context")
        workflow.add_edge("save_context", END)

        return workflow.compile()


    def analyze_image(self, state: VRContextState) -> VRContextState:
        """
        Node to analyze the image using Gemini's vision capabilities
        """
        try:
            if state.get("error"):
                return state
            
            image_data = state["current_image"]
            task = state.get("current_task", "Unknown task")
            step = state.get("task_step", "Unknown step")
            gaze = state.get("gaze_vector", {})
            
            # Create vision prompt with task context
            prompt = f"""You are analyzing an AR passthrough image from a technician.

Task: {task}
Current Step: {step}
Gaze Direction: {gaze}

Analyze the image and identify:
1. What components or objects are visible
2. What the user appears to be focused on (based on gaze)
3. Any potential issues or points of confusion

Be concise and technical. Focus on actionable observations."""
            
            # Create multimodal message for Gemini
            message = HumanMessage(
                content=[
                    {"type": "text", "text": prompt},
                    {
                        "type": "image_url",
                        "image_url": f"data:image/jpeg;base64,{image_data}"
                    }
                ]
            )
            
            # Invoke with retry logic
            try:
                response = self.llm.invoke([message])
            except Exception as e:
                # Retry once
                import time
                time.sleep(1)
                response = self.llm.invoke([message])
            
            state["image_analysis"] = response.content
            
            # Store messages if needed
            if "messages" not in state or state["messages"] is None:
                state["messages"] = []
            state["messages"].append(message)
            state["messages"].append(response)
            
            return state
        except Exception as e:
            state["error"] = f"Image analysis failed: {str(e)}"
            return state
    
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
            context_data = {
                "session_id": state.get("session_id", "unknown"),
                "timestamp": datetime.utcnow().isoformat(),
                "task": state.get("current_task", ""),
                "step": state.get("task_step", ""),
                "gaze_vector": state.get("gaze_vector", {}),
                "image_analysis": state.get("image_analysis", ""),
                "instruction": {
                    "text": state.get("instruction_text", ""),
                    "target_id": state.get("target_id", ""),
                    "haptic_cue": state.get("haptic_cue", "none")
                }
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
        
    def generate_instruction(self, state: VRContextState) -> VRContextState:
        """
        Node to generate instruction based on image analysis and task context
        """
        try:
            if state.get("error"):
                state["instruction_text"] = f"Error occurred: {state['error']}"
                state["target_id"] = ""
                state["haptic_cue"] = "none"
                return state
            
            task = state.get("current_task", "Unknown task")
            step = state.get("task_step", "Unknown step")
            analysis = state.get("image_analysis", "No analysis available")
            gaze = state.get("gaze_vector", {})
            
            # Build prompt with full context
            prompt = f"""You are an expert technical instructor guiding a technician through: {task}

Current Step: {step}
Image Analysis: {analysis}
Gaze: {gaze}

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
            response = self.llm.invoke([message])
            
            # Parse JSON response
            import json
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
            
            state["instruction_text"] = result["instruction_text"]
            state["target_id"] = result.get("target_id", "")
            state["haptic_cue"] = result["haptic_cue"]
            
            # Store messages
            if "messages" not in state or state["messages"] is None:
                state["messages"] = []
            state["messages"].append(message)
            state["messages"].append(response)
            
            return state
        except Exception as e:
            state["error"] = f"Instruction generation failed: {str(e)}"
            state["instruction_text"] = f"Error: {str(e)}"
            state["target_id"] = ""
            state["haptic_cue"] = "none"
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
