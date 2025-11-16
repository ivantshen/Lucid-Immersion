"""
Image processing utilities for the backend API.
"""
import io
from PIL import Image
from typing import Tuple


def compress_image(image_bytes: bytes, target_size: Tuple[int, int] = (768, 768)) -> bytes:
    """
    Compresses image to target size while maintaining aspect ratio.
    
    Args:
        image_bytes: Original image bytes
        target_size: Target size tuple (width, height)
        
    Returns:
        Compressed image bytes (JPEG format)
    """
    img = Image.open(io.BytesIO(image_bytes))
    
    # Convert RGBA to RGB if needed
    if img.mode == 'RGBA':
        img = img.convert('RGB')
    
    # Resize maintaining aspect ratio
    img.thumbnail(target_size, Image.Resampling.LANCZOS)
    
    # Compress to JPEG
    output = io.BytesIO()
    img.save(output, format='JPEG', quality=85, optimize=True)
    
    return output.getvalue()
