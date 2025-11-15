import base64
from datetime import datetime
from typing import TypedDict, List, Optional, Annotated
import operator
from langgraph.graph import StateGraph, MessagesState, START, END
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_core.messages import HumanMessage, AIMessage, SystemMessage

class VRContextState(TypedDict):
    """State for VR context information"""
    current_image: Optional[str]  # base64 encoded image
    current_timestamp: Optional[str]

    # Image analysis
    scene_description: Optional[str]
    detected_objects: Optional[list]
    user_action: Optional[str]

    context_history: Annotated[List[dict], operator.add] # Accumulate context over time

    #LLm instruction and output
    user_query: Optional[str]
    llm_response: Optional[str]

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
        self.workflow = self._build_workflow()

    def _build_workflow(self) -> StateGraph:
        """Build the LangGraph workflow"""

        workflow = StateGraph(VRContextState)

        # Add nodes and edges to the workflow as needed
        workflow.add_node("analyze_image", self.analyze_image)
        workflow.add_node("store_context", self.store_context)
        workflow.add_node("generate_response", self.generate_response)

        workflow.set_entry_point("analyze_image")
        workflow.add_edge("analyze_image", "store_context")
        workflow.add_edge("store_context", "generate_response")
        workflow.add_edge("generate_response", END)

        return workflow.compile()


    def analyze_image(self, state: VRContextState) -> VRContextState:
        """
        Node to analyze the image using Claude's vision capabilities
        """
        try:
            if state.get("error"):
                return state
            
            image_data = state["current_image"]
            
            # Create vision prompt
            messages = [
                HumanMessage(
                    content=[
                        {
                            "type": "image",
                            "source": {
                                "type": "base64",
                                "media_type": "image/jpeg",
                                "data": image_data,
                            },
                        },
                        {
                            "type": "text",
                            "text": """Analyze this VR headset view and provide:
                                1. **A detailed scene description**
                                2. **List of detected objects and their locations**
                                3. **What the user appears to be doing or interacting with**

                                Format your response as:
                                SCENE: <description>
                                OBJECTS: <comma-separated list>
                                ACTION: <user action>"""
                        }
                    ]
                )
            ]
            
            response = self.llm.invoke(messages)
            analysis = response.content
            
            # Parse the analysis
            lines = analysis.split('\n')
            for line in lines:
                if line.startswith('SCENE:'):
                    state["scene_description"] = line.replace('SCENE:', '').strip()
                elif line.startswith('OBJECTS:'):
                    objects = line.replace('OBJECTS:', '').strip()
                    state["detected_objects"] = [obj.strip() for obj in objects.split(',')]
                elif line.startswith('ACTION:'):
                    state["user_action"] = line.replace('ACTION:', '').strip()
            
            return state
        except Exception as e:
            state["error"] = f"Image analysis failed: {str(e)}"
            return state
    
    def store_context(self, state: VRContextState) -> VRContextState:
        """
        Node to store the analyzed context with timestamp
        """
        try:
            if state.get("error"):
                return state
            
            # Create context entry
            context_entry = {
                "timestamp": state["current_timestamp"],
                "scene": state.get("scene_description", ""),
                "objects": state.get("detected_objects", []),
                "action": state.get("user_action", ""),
            }
            
            # Initialize context_history if it doesn't exist
            if "context_history" not in state:
                state["context_history"] = []
            
            # Add to history
            state["context_history"].append(context_entry)
            
            # Trim history if needed
            if len(state["context_history"]) > self.max_context_history:
                state["context_history"] = state["context_history"][-self.max_context_history:]
            
            return state
        except Exception as e:
            state["error"] = f"Context storage failed: {str(e)}"
            return state
        
    def generate_response(self, state: VRContextState) -> VRContextState:
        """
        Node to generate LLM response based on user query and context
        """
        try:
            if state.get("error"):
                state["llm_response"] = f"Error occurred: {state['error']}"
                return state
            
            # Build context summary
            context_summary = self._build_context_summary(state["context_history"])
            
            # Create system message with context
            system_message = f"""You are an AI assistant integrated with a VR headset. You have access to the user's visual context over time.
                Current Context:
                {context_summary}

                Current Situation:
                - Scene: {state.get('scene_description', 'Unknown')}
                - Objects: {', '.join(state.get('detected_objects', []))}
                - User Action: {state.get('user_action', 'Unknown')}
                - Time: {state.get('current_timestamp', 'Unknown')}
                
                Provide helpful, context-aware assistance based on what the user is seeing and doing."""

            # Get user query or use default
            user_query = state.get("user_query", "What should I do next?")
            
            messages = [
                SystemMessage(content=system_message),
                HumanMessage(content=user_query)
            ]
            
            response = self.llm.invoke(messages)
            state["llm_response"] = response.content
            
            return state
        except Exception as e:
            state["error"] = f"Response generation failed: {str(e)}"
            state["llm_response"] = f"Error: {str(e)}"
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
        
    def run(self, image_base64: str, user_query: Optional[str] = None) -> dict:
        """
        Run the workflow with a new image
        
        Args:
            image_base64: Base64 encoded image from VR headset
            user_query: Optional user query for context-aware response
            
        Returns:
            Final state with LLM response
        """
        initial_state = {
            "current_image": image_base64,
            "user_query": user_query,
            "context_history": [],
        }
        
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
