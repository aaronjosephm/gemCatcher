# Unity Git Version Control Guide

## Files to Exclude from Git

When using Git with Unity projects, it's important to exclude certain files and directories to keep your repository clean and efficient. The `.gitignore` file I've created already handles this, but here's an explanation of what's excluded and why:

### 1. Generated and Temporary Files

- **Library/**: Contains cached data that Unity generates. This folder can be very large and is recreated when the project is opened.
- **Temp/**: Contains temporary files generated during Unity's operation.
- **Obj/**: Contains intermediate build files.
- **Build/ and Builds/**: Contains compiled builds of your game.
- **Logs/**: Contains Unity's log files.
- **UserSettings/**: Contains user-specific settings.

### 2. IDE and Editor Files

- **.vs/**, **.vscode/**: Visual Studio and VS Code specific files.
- **\*.csproj**, **\*.sln**, etc.: Project files for IDEs that are generated based on the Unity project.

### 3. Platform-Specific Files

- **\*.apk**, **\*.aab**: Android build files.
- **\*.app**: macOS build files.
- **\*.unitypackage**: Unity package files (unless you specifically want to track these).

### 4. OS Generated Files

- **.DS_Store**: macOS directory metadata files.
- **Thumbs.db**: Windows thumbnail cache files.

## Files to Include in Git

The following files and directories should typically be included in version control:

### 1. Source Code

- **Assets/Scripts/\*.cs**: All your C# script files.
- **Assets/Editor/\*.cs**: Custom editor scripts.

### 2. Project Assets

- **Assets/\*\*/\*.unity**: Scene files.
- **Assets/\*\*/\*.prefab**: Prefab files.
- **Assets/\*\*/\*.asset**: ScriptableObject assets and other Unity assets.
- **Assets/\*\*/\*.mat**: Material files.
- **Assets/\*\*/\*.controller**: Animation controllers.
- **Assets/\*\*/\*.anim**: Animation files.

### 3. Project Settings

- **ProjectSettings/**: Contains project-wide settings that should be shared across the team.
- **Packages/manifest.json**: Defines which packages the project uses.

### 4. Documentation

- **README.md**: Project documentation.
- **Documentation/**: Any additional documentation files.

## Best Practices

1. **Large Binary Files**: Consider using Git LFS (Large File Storage) for large binary files like textures, models, and audio.

2. **Meta Files**: Unity generates `.meta` files for all assets. These should be included in version control as they contain important GUIDs that Unity uses to reference assets.

3. **External Assets**: If you're using assets from the Asset Store, discuss with your team whether to include them in the repository or have each team member download them separately.

4. **Commit Frequency**: Commit often, but make sure the project is in a working state before committing.

5. **Branching Strategy**: Use branches for new features or experiments to avoid breaking the main development branch.

## Using the Current .gitignore

The `.gitignore` file I've created is set up to:

1. Ignore all the standard Unity-generated files and directories
2. Track all C# script files in the Assets/Scripts directory
3. Ignore most other Unity-specific files

If you want to track additional files beyond the scripts, you'll need to modify the `.gitignore` file or explicitly add those files using `git add -f path/to/file`.
