"""
Input validation utilities for the backend API.
"""
import json
import os
from PIL import Image
from typing import Tuple


def validate_image(file) -> Tuple[bool, str]:
    """
    Validates uploaded image file.
    
    Args:
        file: FileStorage object from Flask request
        
    Returns:
        Tuple of (is_valid, error_message)
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


def validate_gaze_vector(gaze_str: str) -> Tuple[bool, dict, str]:
    """
    Validates and parses gaze vector JSON.
    
    Args:
        gaze_str: JSON string with gaze vector
        
    Returns:
        Tuple of (is_valid, parsed_dict, error_message)
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


def sanitize_string(input_str: str) -> str:
    """
    Sanitize string input to prevent injection attacks.
    
    Args:
        input_str: String to sanitize
        
    Returns:
        Sanitized string
    """
    if not input_str:
        return ""
    
    # Remove any null bytes
    sanitized = input_str.replace('\x00', '')
    
    # Strip whitespace
    sanitized = sanitized.strip()
    
    # Limit length to prevent DoS
    max_length = 256
    if len(sanitized) > max_length:
        sanitized = sanitized[:max_length]
    
    return sanitized
