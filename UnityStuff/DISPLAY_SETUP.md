# HeadlessConverter Display Setup

## Overview
The HeadlessConverter now displays AI responses in a structured, easy-to-read format with three text components.

## UI Components Required

In your Unity scene, you need to assign **3 TextMeshPro (TMP_Text) components** to the HeadlessConverter script:

### 1. Header Text
- **Purpose**: Displays the image analysis (what the AI sees)
- **Example**: "The user is wearing a Meta Quest 3 headset and holding both controllers..."
- **Recommended Style**: 
  - Font Size: 24-28
  - Font Weight: Bold
  - Color: White or bright color
  - Alignment: Left or Center

### 2. Subheader Text
- **Purpose**: Displays the step number and timestamp
- **Example**: "Step 1 | Nov 16, 09:54:21"
- **Recommended Style**:
  - Font Size: 18-20
  - Font Weight: Regular or Semi-Bold
  - Color: Light gray or secondary color
  - Alignment: Left

### 3. Instruction Text
- **Purpose**: Displays the numbered list of instruction steps
- **Example**:
  ```
  Instructions:
  
  1. Ensure the Meta Quest 3 headset is comfortably positioned...
  
  2. Confirm both controllers are powered on...
  
  3. Look straight ahead to locate the main menu...
  
  4. Use the trigger button on your right controller...
  ```
- **Recommended Style**:
  - Font Size: 16-20
  - Font Weight: Regular
  - Color: White
  - Alignment: Left
  - Enable Word Wrapping
  - Vertical Overflow: Scroll (if content is long)

## Setup Steps

1. **Create UI Canvas** (if you don't have one):
   - Right-click in Hierarchy → UI → Canvas
   - Set Canvas to "World Space" for VR
   - Position it in front of the user

2. **Create Text Elements**:
   - Right-click on Canvas → UI → Text - TextMeshPro (3 times)
   - Name them: "HeaderText", "SubheaderText", "InstructionText"

3. **Position and Style**:
   - Arrange them vertically with appropriate spacing
   - Apply the recommended styles above

4. **Assign to Script**:
   - Select the GameObject with HeadlessConverter script
   - Drag each text element to its corresponding field:
     - Header Text → headerText
     - Subheader Text → subheaderText
     - Instruction Text → instructionText

5. **Optional Debug Text**:
   - Create another TMP_Text for debug messages
   - Assign to debugStatusText field

## Response Format

The backend now returns:
```json
{
  "status": "success",
  "session_id": "abc-123...",
  "instruction_id": "abc-123-1",
  "instruction_steps": [
    "Step 1 instruction",
    "Step 2 instruction",
    "Step 3 instruction"
  ],
  "target_id": "",
  "haptic_cue": "none",
  "image_analysis": "The user is wearing...",
  "timestamp": "2025-11-16T09:54:21.953914Z"
}
```

## Testing

1. Deploy the updated backend to Cloud Run
2. Build and deploy to Meta Quest
3. Press the A button to capture and send an image
4. Watch the UI populate with:
   - Header: What the AI sees
   - Subheader: Step and time
   - Instructions: Numbered list of steps

## Troubleshooting

- **Text not appearing**: Check that TMP_Text components are assigned in Inspector
- **Text cut off**: Increase the RectTransform size or enable scrolling
- **Wrong format**: Ensure backend is updated and deployed with new response structure
