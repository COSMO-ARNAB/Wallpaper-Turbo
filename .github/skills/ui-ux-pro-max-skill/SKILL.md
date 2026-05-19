---
name: ui-ux-pro-max-skill
user-invocable: true
description: 'A skill to guide users in creating and managing UI/UX workflows for Wallpaper Turbo.'
---

# UI/UX Pro Max Skill

## Purpose
This skill provides a structured workflow for managing UI/UX tasks in the Wallpaper Turbo project. It includes steps for designing, implementing, and testing user interfaces and experiences.

## Workflow

### Step 1: Define Requirements
- **Action**: Gather user requirements for the UI/UX feature.
- **Tools**: Use the `ask-questions` tool to clarify ambiguous requirements.
- **Output**: A clear list of UI/UX goals.

### Step 2: Design Mockups
- **Action**: Create wireframes or mockups for the feature.
- **Tools**: Use external design tools (e.g., Figma, Adobe XD) or ASCII art for simple layouts.
- **Output**: Approved mockups.

### Step 3: Implement UI Components
- **Action**: Develop the UI components based on the mockups.
- **Tools**: Use the `insert_edit_into_file` tool to add or modify code.
- **Output**: Functional UI components.

### Step 4: Integrate with Backend
- **Action**: Connect the UI components to the backend logic.
- **Tools**: Use the `read_file` and `insert_edit_into_file` tools to understand and modify backend code.
- **Output**: Fully integrated UI.

### Step 5: Test and Iterate
- **Action**: Test the UI/UX for usability and functionality.
- **Tools**: Use manual testing and automated test scripts.
- **Output**: A polished and user-friendly interface.

## Decision Points
- **Mockup Approval**: Ensure mockups are approved before implementation.
- **Integration Testing**: Verify backend integration before finalizing the UI.

## Quality Criteria
- **Usability**: The interface should be intuitive and easy to use.
- **Performance**: The UI should load quickly and respond smoothly.
- **Consistency**: The design should align with the project's style guide.

## Example Prompts
- "Guide me through creating a new settings page."
- "Help me test the user onboarding flow."

## Related Customizations
- Consider creating a `settings-page-skill` for managing settings-specific workflows.
- Develop a `ui-testing-skill` for automated UI testing.