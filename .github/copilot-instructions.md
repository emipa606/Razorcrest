# Razorcrest (Continued) Mod - GitHub Copilot Instructions

## Mod Overview

The Razorcrest (Continued) mod brings the iconic Razorcrest ship from "The Mandalorian" series into RimWorld. It offers players the chance to embark on bounty hunter adventures in the Star Wars universe, with the Razorcrest ready to take flight across the Rim. This mod is an update of Topkeks’ original creation and is designed for seamless integration with RimWorld's SRTS Expanded mod, requiring research into "Mandorian Flight." 

## Purpose

The primary purpose of this mod is to enhance the gameplay experience by introducing a legendary starship with unique features and capabilities. The Razorcrest is designed for both travel and combat, offering new tactical opportunities and immersive gameplay for fans of Star Wars and RimWorld.

## Key Features and Systems

- **Personnel and Cargo Capacity**: The Razorcrest can hold up to 8 personnel and a bomb capacity of 24 with a good accuracy level.
- **Travel and Combat**: While traveling slowly, it compensates with good fuel efficiency and a maximum payload capacity for various missions.
- **Power Source**: The ship is equipped with an internal power plant producing 1000w for operational efficiency.
- **Self-Defense Mechanism**: The front guns can engage enemies within a 100° cone, aiding in defense while exploring or in battle.
- **Control Features**: Includes a "Hold Fire" feature to manage combat engagements.

## Coding Patterns and Conventions

- Follow .NETFramework version 4.8 standards and C# conventions.
- Class and method names are styled in PascalCase.
- Use meaningful method names that clearly communicate their functionality, e.g., `throwDebugText`, `OrderAttack`.
- Maintain modular code with specific responsibilities, isolating methods for specific actions.

## XML Integration

- Integration with RimWorld's XML systems is crucial for defining new items, buildings, research projects, etc.
- Use `<DefModExtension>` to extend existing definitions with additional properties as needed, exemplified by `TurretPosOffset`.

## Harmony Patching

- Utilize Harmony to patch existing RimWorld methods when altering game mechanics, such as modifying turret behavior or implementing new attack patterns.
- Ensure patches are non-invasive and reversible to not disrupt the core game functionality.
- Document any changes made to the game’s original methods clearly.

## Suggestions for Copilot

- **General Coding**: Assist in generating repetitive code patterns such as method stubs and interface implementations for new classes and methods.
- **Debugging**: Facilitate generating detailed debug text output using methods like `throwDebugText`.
- **Harmony Integration**: Help in setting up Harmony patches by suggesting common patch templates and best practices in modding RimWorld.
- **XML Handling**: Provide template suggestions for XML files and DefModExtensions used in integrating new features with the game.
- **Class Implementations**: Propose class structures and method definitions for common interfaces like `IAttackTargetSearcher` to ensure conformity to expected game behaviors.

By integrating these instructions and leveraging GitHub Copilot effectively, developers can enhance the functionality and maintainability of the Razorcrest (Continued) mod while ensuring high-quality code practices.

## Project Solution Guidelines
- Relevant mod XML files are included as Solution Items under the solution folder named XML, these can be read and modified from within the solution.
- Use these in-solution XML files as the primary files for reference and modification.
- The `.github/copilot-instructions.md` file is included in the solution under the `.github` solution folder, so it should be read/modified from within the solution instead of using paths outside the solution. Update this file once only, as it and the parent-path solution reference point to the same file in this workspace.
- When making functional changes in this mod, ensure the documented features stay in sync with implementation; use the in-solution `.github` copy as the primary file.
- In the solution is also a project called Assembly-CSharp, containing a read-only version of the decompiled game source, for reference and debugging purposes.
- For any new documentation, update this copilot-instructions.md file rather than creating separate documentation files.
