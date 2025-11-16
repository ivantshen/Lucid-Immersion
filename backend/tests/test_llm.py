"""
Tests for the VRContextWorkflow in llm.py
"""
import pytest
from unittest.mock import Mock, patch, MagicMock
import sys
import os
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from llm import VRContextWorkflow, VRContextState


class TestVRContextWorkflow:
    """Test suite for VRContextWorkflow"""
    
    @pytest.fixture
    def workflow(self):
        """Create a VRContextWorkflow instance for testing"""
        return VRContextWorkflow(api_key="test-api-key")
    
    @pytest.fixture
    def sample_state(self):
        """Create a sample state for testing"""
        return {
            "current_image": "base64_encoded_image_data",
            "task_step": "4",
            "current_task": "PSU_Install",
            "gaze_vector": {"x": 0.5, "y": -0.2, "z": 0.8},
            "session_id": "test-session-123",
            "context_history": [],
            "messages": []
        }
    
    def test_workflow_initialization(self, workflow):
        """Test that workflow initializes correctly"""
        assert workflow is not None
        assert workflow.llm is not None
        assert workflow.workflow is not None
        assert workflow.max_context_history == 10
    
    @patch('llm.ChatGoogleGenerativeAI')
    def test_analyze_image_node(self, mock_llm_class, workflow, sample_state):
        """Test the analyze_image node"""
        # Mock the LLM response
        mock_response = Mock()
        mock_response.content = "The image shows a server chassis with visible power supply bay."
        
        mock_llm_instance = Mock()
        mock_llm_instance.invoke.return_value = mock_response
        workflow.llm = mock_llm_instance
        
        # Run the node
        result = workflow.analyze_image(sample_state)
        
        # Verify
        assert "image_analysis" in result
        assert result["image_analysis"] == mock_response.content
        assert "messages" in result
        assert len(result["messages"]) == 2  # HumanMessage and AIMessage
    
    @patch('llm.ChatGoogleGenerativeAI')
    def test_analyze_image_with_retry(self, mock_llm_class, workflow, sample_state):
        """Test that analyze_image retries on failure"""
        # Mock the LLM to fail once then succeed
        mock_response = Mock()
        mock_response.content = "Analysis after retry"
        
        mock_llm_instance = Mock()
        mock_llm_instance.invoke.side_effect = [Exception("API Error"), mock_response]
        workflow.llm = mock_llm_instance
        
        # Run the node
        result = workflow.analyze_image(sample_state)
        
        # Verify it retried and succeeded
        assert "image_analysis" in result
        assert result["image_analysis"] == "Analysis after retry"
        assert mock_llm_instance.invoke.call_count == 2
    
    @patch('llm.ChatGoogleGenerativeAI')
    def test_generate_instruction_node(self, mock_llm_class, workflow, sample_state):
        """Test the generate_instruction node"""
        # Add image_analysis to state
        sample_state["image_analysis"] = "Server chassis visible, power supply bay open"
        
        # Mock the LLM response with JSON
        mock_response = Mock()
        mock_response.content = '''{
            "instruction_text": "Locate the 8-pin PDU cable and plug it into port J_PWR_1.",
            "target_id": "J_PWR_1",
            "haptic_cue": "guide_to_target"
        }'''
        
        mock_llm_instance = Mock()
        mock_llm_instance.invoke.return_value = mock_response
        workflow.llm = mock_llm_instance
        
        # Run the node
        result = workflow.generate_instruction(sample_state)
        
        # Verify
        assert "instruction_text" in result
        assert "target_id" in result
        assert "haptic_cue" in result
        assert result["target_id"] == "J_PWR_1"
        assert result["haptic_cue"] == "guide_to_target"
    
    @patch('llm.ChatGoogleGenerativeAI')
    def test_generate_instruction_invalid_json_fallback(self, mock_llm_class, workflow, sample_state):
        """Test that generate_instruction handles invalid JSON gracefully"""
        sample_state["image_analysis"] = "Test analysis"
        
        # Mock the LLM to return non-JSON
        mock_response = Mock()
        mock_response.content = "This is not JSON, just plain text instruction"
        
        mock_llm_instance = Mock()
        mock_llm_instance.invoke.return_value = mock_response
        workflow.llm = mock_llm_instance
        
        # Run the node
        result = workflow.generate_instruction(sample_state)
        
        # Verify fallback behavior
        assert "instruction_text" in result
        assert result["instruction_text"] == mock_response.content[:200]
        assert result["target_id"] == ""
        assert result["haptic_cue"] == "none"
    
    @patch('llm.ChatGoogleGenerativeAI')
    def test_generate_instruction_validates_haptic_cue(self, mock_llm_class, workflow, sample_state):
        """Test that invalid haptic_cue values are corrected"""
        sample_state["image_analysis"] = "Test analysis"
        
        # Mock the LLM to return invalid haptic_cue
        mock_response = Mock()
        mock_response.content = '''{
            "instruction_text": "Test instruction",
            "target_id": "TEST_1",
            "haptic_cue": "invalid_cue"
        }'''
        
        mock_llm_instance = Mock()
        mock_llm_instance.invoke.return_value = mock_response
        workflow.llm = mock_llm_instance
        
        # Run the node
        result = workflow.generate_instruction(sample_state)
        
        # Verify haptic_cue was corrected to "none"
        assert result["haptic_cue"] == "none"
    
    @patch('builtins.open', create=True)
    @patch('os.makedirs')
    def test_save_context_node(self, mock_makedirs, mock_open, workflow, sample_state):
        """Test the save_context node"""
        # Add required fields to state
        sample_state["image_analysis"] = "Test analysis"
        sample_state["instruction_text"] = "Test instruction"
        sample_state["target_id"] = "TEST_1"
        sample_state["haptic_cue"] = "none"
        
        # Mock file operations
        mock_file = MagicMock()
        mock_open.return_value.__enter__.return_value = mock_file
        
        # Run the node
        result = workflow.save_context(sample_state)
        
        # Verify directory creation
        mock_makedirs.assert_called_once_with("contexts", exist_ok=True)
        
        # Verify file was opened for writing
        mock_open.assert_called_once()
        call_args = mock_open.call_args[0]
        assert call_args[0] == "contexts/test-session-123.json"
        assert call_args[1] == "w"
        
        # Verify context_history was updated
        assert "context_history" in result
        assert len(result["context_history"]) > 0
    
    @patch('llm.ChatGoogleGenerativeAI')
    def test_full_workflow_run(self, mock_llm_class, workflow):
        """Test running the full workflow"""
        # Mock LLM responses
        mock_analysis_response = Mock()
        mock_analysis_response.content = "Server chassis with power supply bay"
        
        mock_instruction_response = Mock()
        mock_instruction_response.content = '''{
            "instruction_text": "Connect the power cable",
            "target_id": "PWR_1",
            "haptic_cue": "guide_to_target"
        }'''
        
        mock_llm_instance = Mock()
        mock_llm_instance.invoke.side_effect = [
            mock_analysis_response,
            mock_instruction_response
        ]
        workflow.llm = mock_llm_instance
        
        # Mock file operations for save_context
        with patch('builtins.open', create=True), \
             patch('os.makedirs'):
            
            # Run the workflow
            result = workflow.run(
                image_base64="test_image_base64",
                task_step="5",
                current_task="Cable_Install",
                gaze_vector={"x": 0.1, "y": 0.2, "z": 0.3},
                session_id="test-session-456"
            )
        
        # Verify result contains expected fields
        assert "instruction_text" in result
        assert "target_id" in result
        assert "haptic_cue" in result
        assert result["session_id"] == "test-session-456"
        assert result["current_task"] == "Cable_Install"
        assert result["task_step"] == "5"
