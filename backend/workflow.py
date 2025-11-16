"""
LangGraph workflow for VR context processing.
Handles the orchestration of image analysis, instruction generation, and context saving.
"""
import base64
import json
import os
from datetime import datetime
from typing import Optional
from langgraph.graph import StateGraph, END
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_core.messages import HumanMessage


class WorkflowNodes:
    """
    Collection of LangGraph nodes for the VR context workflow.
    Each node is a step in the processing pipeline.
    """
    
    def __init__(self, llm: ChatGoogleGenerativeAI):
        """
        Initialize workflow nodes with an LLM instance.
        
        Args:
            llm: ChatGoogleGenerativeAI instance for API calls
        """
        self.llm = llm
    
    def analyze_image(self, state: dict) -> dict:
        """
        Node to analyze the image using Gemini's vision capabilities.
        
        Args:
            state: Current workflow state containing image and context
            
        Returns:
            Updated state with image_analysis field
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
                print(f"Gemini API error, retrying: {e}")
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
    
    def generate_instruction(self, state: dict) -> dict:
        """
        Node to generate instruction based on image analysis and task context.
        
        Args:
            state: Current workflow state with image_analysis
            
        Returns:
            Updated state with instruction_text, target_id, and haptic_cue
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
    
    def save_context(self, state: dict) -> dict:
        """
        Node to save the context to JSON file.
        
        Args:
            state: Current workflow state with all processed data
            
        Returns:
            Updated state with context saved to file
        """
        try:
            if state.get("error"):
                return state
            
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
            
            return state
        except Exception as e:
            state["error"] = f"Context save failed: {str(e)}"
            return state


def build_workflow(llm: ChatGoogleGenerativeAI, max_context_history: int = 10) -> StateGraph:
    """
    Build the LangGraph workflow for VR context processing.
    
    The workflow follows this sequence:
    1. analyze_image: Analyze AR image with Gemini Vision
    2. generate_instruction: Generate step-by-step instructions
    3. save_context: Persist context to JSON file
    
    Args:
        llm: ChatGoogleGenerativeAI instance for API calls
        max_context_history: Maximum number of context entries to keep in memory
        
    Returns:
        Compiled StateGraph workflow ready for execution
    """
    # Import here to avoid circular dependency
    import sys
    import os
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    from state import VRContextState
    
    # Initialize workflow with state
    workflow = StateGraph(VRContextState)
    
    # Create nodes instance
    nodes = WorkflowNodes(llm)
    
    # Add nodes to workflow
    workflow.add_node("analyze_image", nodes.analyze_image)
    workflow.add_node("generate_instruction", nodes.generate_instruction)
    workflow.add_node("save_context", nodes.save_context)
    
    # Define workflow edges (execution order)
    workflow.set_entry_point("analyze_image")
    workflow.add_edge("analyze_image", "generate_instruction")
    workflow.add_edge("generate_instruction", "save_context")
    workflow.add_edge("save_context", END)
    
    # Compile and return
    return workflow.compile()


def create_workflow(api_key: str, model: str = "gemini-1.5-flash", 
                   temperature: float = 0.2, max_tokens: int = 1024) -> StateGraph:
    """
    Convenience function to create a complete workflow with default settings.
    
    This is a standalone way to create the workflow without using VRContextWorkflow class.
    
    Args:
        api_key: Google Gemini API key
        model: Gemini model to use (default: gemini-1.5-flash)
        temperature: LLM temperature for response generation
        max_tokens: Maximum tokens for LLM responses
        
    Returns:
        Compiled workflow ready for execution
        
    Example:
        >>> workflow = create_workflow(api_key="your-key")
        >>> result = workflow.invoke(initial_state)
    """
    llm = ChatGoogleGenerativeAI(
        model=model,
        google_api_key=api_key,
        max_tokens=max_tokens,
        temperature=temperature
    )
    
    return build_workflow(llm)


# Example usage
if __name__ == "__main__":
    import os
    from dotenv import load_dotenv
    
    load_dotenv()
    
    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        print("Please set GEMINI_API_KEY environment variable")
        exit(1)
    
    # Create workflow
    workflow = create_workflow(api_key)
    
    print("Workflow created successfully!")
    print("\nWorkflow structure:")
    print("1. analyze_image - Analyze AR image with Gemini Vision")
    print("2. generate_instruction - Generate step-by-step instructions")
    print("3. save_context - Persist context to JSON file")
    print("\nTo use: workflow.invoke(initial_state)")
