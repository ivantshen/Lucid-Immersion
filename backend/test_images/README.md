# Test Images Folder

This folder is for storing test images to use with the backend API.

## Supported File Types

### ✅ Accepted Formats
- **JPEG** (`.jpg`, `.jpeg`) - Recommended
- **PNG** (`.png`) - Supported

### ❌ Not Accepted
- GIF, BMP, TIFF, WebP, SVG, or any other formats
- The backend will reject these with a 400 error

## File Size Restrictions

### Maximum Size: **5MB**
- Files larger than 5MB will be rejected with a 413 error
- Files between 1MB-5MB will be automatically compressed to under 1MB
- Files under 1MB are sent as-is

### Recommended Size: **500KB - 2MB**
- Provides good quality without excessive compression
- Faster upload and processing times

## Image Requirements

### Resolution
- **Recommended**: 1024x1024 or 768x768
- **Minimum**: 512x512 (for decent analysis)
- **Maximum**: Any size up to 5MB file limit
- Images are automatically resized to 768x768 if over 1MB

### Content Guidelines
For best results with the AR assistance system:
- ✅ Clear, well-lit images
- ✅ Focus on technical equipment/components
- ✅ Include visible labels or part numbers
- ✅ Show the work area clearly
- ❌ Avoid blurry or dark images
- ❌ Avoid images with excessive motion blur

## Example Test Scenarios

### 1. Server Installation
- Image of server chassis
- Power supply bay visible
- Cables and ports in view

### 2. Cable Management
- Image of cable routing
- Connection points visible
- Labels readable

### 3. Component Assembly
- Image of components to be assembled
- Clear view of parts
- Assembly area visible

## How to Use Test Images

### Option 1: Using curl
```bash
curl -X POST http://localhost:8080/assist \
  -H "Authorization: Bearer your-api-key" \
  -F "snapshot=@test_images/server_chassis.jpg" \
  -F "task_step=4" \
  -F "current_task=PSU_Install" \
  -F 'gaze_vector={"x": 0.5, "y": -0.2, "z": 0.8}'
```

### Option 2: Using Python
```python
import requests

with open('test_images/server_chassis.jpg', 'rb') as img:
    files = {'snapshot': img}
    data = {
        'task_step': '4',
        'current_task': 'PSU_Install',
        'gaze_vector': '{"x": 0.5, "y": -0.2, "z": 0.8}'
    }
    headers = {'Authorization': 'Bearer your-api-key'}
    
    response = requests.post(
        'http://localhost:8080/assist',
        files=files,
        data=data,
        headers=headers
    )
    print(response.json())
```

### Option 3: Using Postman
1. Set method to POST
2. URL: `http://localhost:8080/assist`
3. Headers: `Authorization: Bearer your-api-key`
4. Body → form-data:
   - `snapshot`: [Select file from test_images/]
   - `task_step`: `4`
   - `current_task`: `PSU_Install`
   - `gaze_vector`: `{"x": 0.5, "y": -0.2, "z": 0.8}`

## Sample Images to Add

You can add images like:
- `server_chassis.jpg` - Server with open bay
- `power_cable.jpg` - Power cable connection
- `network_port.jpg` - Network port close-up
- `component_assembly.jpg` - Parts to be assembled
- `tool_setup.jpg` - Tools and workspace

## Validation Details

The backend validates images using `app/utils/validation.py`:

```python
# Checks performed:
1. Content-Type must be 'image/jpeg' or 'image/png'
2. File size must be ≤ 5MB
3. File must be a valid image (PIL can open it)
4. File must not be corrupted
```

## Compression Details

If your image is > 1MB, it will be compressed:
- Resized to 768x768 (maintains aspect ratio)
- Converted to JPEG with quality=85
- RGBA images converted to RGB
- Optimized for smaller file size

## Tips for Best Results

### Image Quality
- Use good lighting
- Keep camera steady
- Focus on the subject
- Avoid reflections and glare

### File Preparation
- Save as JPEG for smaller file sizes
- Use PNG only if transparency is needed
- Compress large images before uploading
- Test with various image sizes

### Testing Strategy
1. Start with small, clear images (< 500KB)
2. Test with larger images (1-3MB) to verify compression
3. Test with edge cases (very small, near 5MB limit)
4. Test with different content types (equipment, cables, etc.)

## Error Messages You Might See

### "Invalid image type. Must be JPEG or PNG"
- Your file is not a JPEG or PNG
- Check the file extension and actual format

### "Image too large. Maximum 5MB"
- Your file exceeds 5MB
- Compress the image before uploading

### "Invalid image file"
- The file is corrupted or not a valid image
- Try re-saving or using a different image

## Current Test Images

(Add your images here and list them)

- [ ] `example1.jpg` - Description
- [ ] `example2.png` - Description
- [ ] `example3.jpg` - Description

---

**Ready to test!** Add your JPEG or PNG images (under 5MB) to this folder and use them with the API.
