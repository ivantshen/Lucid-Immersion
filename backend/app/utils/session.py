"""
Session context management utilities.
"""
import json
import os
from pathlib import Path
from typing import Dict, Optional


def load_session_context(session_id: str, context_dir: str = "contexts") -> Optional[Dict]:
    """
    Load and parse session context from JSON file.
    
    Args:
        session_id: The session identifier
        context_dir: Directory where context files are stored (default: "contexts")
    
    Returns:
        Dict with session context including:
        - session_id: str
        - task: str
        - step: str
        - image_analysis: str
        - instruction: dict with steps, target_id, haptic_cue
        - timestamp: str
        - gaze_vector: dict
        
        Returns None if session file doesn't exist
    
    Raises:
        ValueError: If the session file exists but contains invalid JSON
    """
    # Build file path
    context_path = Path(context_dir) / f"{session_id}.json"
    
    # Check if file exists
    if not context_path.exists():
        return None
    
    # Load and parse JSON
    try:
        with open(context_path, 'r') as f:
            context_data = json.load(f)
        
        # Validate required fields
        required_fields = ['session_id', 'task', 'step', 'image_analysis', 'instruction']
        missing_fields = [field for field in required_fields if field not in context_data]
        
        if missing_fields:
            raise ValueError(f"Session context missing required fields: {', '.join(missing_fields)}")
        
        return context_data
        
    except json.JSONDecodeError as e:
        raise ValueError(f"Invalid JSON in session file: {str(e)}")
    except Exception as e:
        raise ValueError(f"Error loading session context: {str(e)}")
