# Gem Catcher Game

A Unity game where players catch falling gems to score points.

## Game Dynamics

The game now features a more strategic gameplay experience:

1. **Prediction-Based Gameplay**

   - Only one gem spawns at a time
   - Gems fall slowly for the first 3 seconds (placement phase)
   - You must predict where the gem will land based on its initial trajectory
   - After 3 seconds, the gem speeds up to normal velocity

2. **Dynamic Gem Movement**

   - Gems move with a higher probability of diagonal trajectories
   - Gems can bounce off obstacles, changing their path unpredictably
   - Trajectory prediction line helps you estimate the gem's initial path

3. **Obstacle System**

   - Various obstacles appear on the screen
   - Obstacles can be static, rotating, or moving
   - Gems bounce off obstacles, creating unpredictable paths
   - Strategic positioning is key to success

4. **Catcher Placement**
   - You have 3 seconds to place your catcher at the bottom
   - If you don't place it within the time limit, it will be placed randomly
   - A timer shows the remaining placement time
   - The catcher resets to the middle position when a new gem spawns

## Improvements Made

### Core Gameplay Improvements

1. **Scoring System**

   - Added a score counter that increases when gems are caught
   - Implemented high score tracking using PlayerPrefs
   - Added win condition when reaching a target score

2. **Difficulty Progression**

   - Implemented difficulty levels that increase as the player scores more points
   - Gems fall faster at higher difficulty levels
   - Obstacles become more challenging

3. **UI Enhancements**

   - Added score display
   - Added high score display
   - Added placement timer display
   - Implemented game over screen with final score
   - Added restart functionality

4. **Sound System**
   - Added sound manager for playing sound effects
   - Implemented sound effects for catching gems and bouncing off obstacles
   - Added background music support
   - Added game over and win sounds

### Technical Improvements

1. **GemCatcher.cs**

   - Fixed empty Start() method
   - Cached references to improve performance
   - Added proper gem radius calculation
   - Implemented score tracking and events

2. **CatcherManager.cs**

   - Improved efficiency by moving the catcher instead of destroying and recreating it
   - Added visual feedback for selected slots
   - Added trajectory prediction system
   - Implemented placement phase timer display
   - Added random catcher placement if user doesn't place it in time

3. **ObjectPooler.cs**

   - Modified to spawn only one gem at a time
   - Implemented two-speed system (slow during placement, normal after)
   - Added obstacle spawning and management
   - Enhanced difficulty progression system

4. **FallingObject.cs**

   - Added variable speed functionality
   - Improved boundary checking logic
   - Enhanced collision detection with obstacles
   - Implemented more diagonal movement

5. **BoundaryManager.cs**

   - Removed debug logs
   - Added option to visualize boundaries
   - Implemented automatic boundary creation if not assigned

6. **New Scripts**
   - **UIManager.cs**: Handles all UI elements and game state
   - **SoundManager.cs**: Manages sound effects and music
   - **Obstacle.cs**: Controls obstacle behavior and interactions

## How to Play

1. Wait for a gem to appear at the top of the screen
2. The gem will start falling slowly
3. You have 3 seconds to position your catcher at the bottom
4. Watch the trajectory line to help predict where the gem will fall
5. Click on a slot at the bottom to position your catcher
6. If you don't place the catcher within 3 seconds, it will be placed randomly
7. After the placement phase, the gem will speed up
8. Gems will bounce off obstacles, creating unpredictable paths
9. Catch the gem to score points
10. Reach the target score to win the game

## Future Improvements

- Add particle effects when catching gems
- Implement different gem types with varying point values
- Add power-ups that provide special abilities
- Create multiple levels with increasing difficulty
- Add mobile touch controls for better mobile gameplay
