# Timer Text Setup Instructions

To make the gem speedup timer visible in your game, follow these steps:

## Option 1: Create a Text Object in the Unity Editor (Recommended)

1. In the Unity Editor, select your Canvas in the Hierarchy
2. Right-click on the Canvas and select **UI → Text**
3. Rename this new Text object to "GemSpeedupTimer"
4. Position it at the top center of your screen
5. In the Inspector, set the following properties:
   - **Text**: Leave blank or set to "GEM SPEED UP IN: 3.0s" for testing
   - **Font Size**: 48
   - **Color**: White
   - **Alignment**: Center
   - **Horizontal Overflow**: Overflow
   - **Vertical Overflow**: Overflow
6. Add an **Outline** component to the Text object:
   - Click "Add Component" at the bottom of the Inspector
   - Search for "Outline" and add it
   - Set the **Effect Color** to Black
   - Set the **Effect Distance** to (2, 2)
7. Select your UIManager GameObject in the Hierarchy
8. In the Inspector, drag the GemSpeedupTimer Text object into the "Gem Speedup Timer Text" field

## Option 2: Enable Automatic Creation

If you prefer to have the text created automatically:

1. Select your UIManager GameObject in the Hierarchy
2. In the Inspector, check the "Create Timer Text Automatically" checkbox
3. Make sure your scene has a Canvas object (the script will try to find one)
4. Play the game - the timer text should be created automatically

## Troubleshooting

If you still don't see the timer text:

1. Check the Console for any error messages
2. Make sure your Canvas is set to "Screen Space - Overlay" or "Screen Space - Camera" mode
3. Verify that the Canvas is active and visible
4. Check that the Text object is a child of the Canvas
5. Make sure the Text object is positioned within the visible area of the screen
6. Verify that the Text object is active (checkbox next to its name in the Hierarchy is checked)
7. Try increasing the font size to make it more visible (e.g., 72 instead of 48)
8. Try changing the text color to a more visible color (e.g., yellow instead of white)

## Testing the Timer

To test if the timer is working correctly:

1. Make sure the UIManager script is attached to a GameObject in your scene
2. Make sure the ObjectPooler script is properly broadcasting the placement phase events
3. Play the game and spawn a new gem
4. The timer should appear at the top of the screen and count down from 3.0 seconds
5. When the countdown reaches 0, the timer should disappear and the gem should speed up
