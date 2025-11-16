# Instruction Format Update

## Change Summary

Updated the backend to return **a list of instruction steps** instead of a single text string.

## What Changed

### 1. **Prompt Update** (`llm.py`)
**Before:**
```json
{
  "instruction": {
    "text": "Single instruction sentence"
  }
}
```

**After:**
```json
{
  "instruction": {
    "steps": [
      "First instruction step",
      "Second instruction step", 
      "Third instruction step"
    ]
  }
}
```

### 2. **State Type** (`llm.py`)
```python
# Before
instruction_text: Optional[str]

# After  
instruction_text: Optional[List[str]]  # List of instruction steps
```

### 3. **API Response** (`app/routes/assist.py`)
**Before:**
```json
{
  "status": "success",
  "step_text": "Single instruction"
}
```

**After:**
```json
{
  "status": "success",
  "instruction_steps": [
    "Step 1",
    "Step 2",
    "Step 3"
  ]
}
```

### 4. **Context Storage** (`llm.py` - save_context)
**Before:**
```json
{
  "instruction": {
    "text": "Single instruction"
  }
}
```

**After:**
```json
{
  "instruction": {
    "steps": [
      "Step 1",
      "Step 2"
    ]
  }
}
```

## Benefits

1. **More Detailed Guidance**: Users get step-by-step instructions instead of a single sentence
2. **Better AR Display**: Unity can display steps sequentially or as a checklist
3. **Progress Tracking**: Can mark individual steps as complete
4. **Clearer Instructions**: Breaking down complex tasks into discrete steps

## Example Response

### Request:
```bash
POST /assist
- Image: User at gym with dumbbells
- Task: "Gym Biceps Curl"
- Step: "1"
```

### Response:
```json
{
  "status": "success",
  "session_id": "abc-123",
  "instruction_id": "abc-123-1",
  "instruction_steps": [
    "Sit on the bench with your back straight and feet flat on the floor",
    "Hold a dumbbell in each hand with palms facing forward",
    "Keep your elbows close to your torso",
    "Slowly curl the weights up toward your shoulders",
    "Pause at the top, then lower back down with control"
  ],
  "target_id": "dumbbells",
  "haptic_cue": "guide_to_target"
}
```

## Unity Integration

Unity should now handle the response as:

```csharp
// Parse response
var response = JsonUtility.FromJson<AssistResponse>(jsonResponse);

// Display steps
foreach (var step in response.instruction_steps) {
    DisplayInstructionStep(step);
}

// Or display as numbered list
for (int i = 0; i < response.instruction_steps.Length; i++) {
    DisplayText($"{i+1}. {response.instruction_steps[i]}");
}
```

## Backward Compatibility

The code includes fallback logic:
- If Gemini returns old format with `"text"` field, it converts to a single-item list
- If `instruction_text` is a string, it's automatically converted to `[string]`
- This ensures no breaking changes if the LLM doesn't follow the new format

## Testing

Test with:
```bash
cd backend
python test_with_image.py
```

The test script now displays:
```
📝 Instructions:
   1. First step
   2. Second step
   3. Third step
```

## Files Modified

1. `backend/llm.py` - Prompt, parsing, state type, save_context
2. `backend/app/routes/assist.py` - Response formatting
3. `backend/test_with_image.py` - Display formatting
4. `backend/state.py` - Type definition (if using modular approach)

## Migration Notes

If you have existing Unity code expecting `step_text`, update to use `instruction_steps`:

```csharp
// Old
string instruction = response.step_text;

// New
string[] instructions = response.instruction_steps;
```
