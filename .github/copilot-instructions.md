# .github/copilot-instructions.md

## Mod Overview and Purpose
This mod introduces a new class of ships equipped with turrets to the game RimWorld. These ships offer enhanced combat features and strategic combat management for players looking to enhance their defense and offense capabilities.

## Key Features and Systems
- **Ship With Turret:** Implements a ship capable of engaging targets using mounted turrets.
- **Projectile Launching:** Custom verb extensions for launching projectiles.
- **Targeting System:** Implements comprehensive targeting logic for engaging enemy threats.
- **Attack Management:** Detailed control and logic for managing attacks including start, burst, completion, and cooldown mechanics.

## Coding Patterns and Conventions
- **Class Design:** Follows an inheritance-based design where advanced behavior (e.g., `Verb_Shoot`) extends base functionality (`Verb_LaunchProjectile`).
- **Visibility Modifiers:** Uses appropriate access modifiers like `public`, `protected`, and `private` to ensure encapsulation and modularity.
- **CamelCase Naming:** Classes and methods use CamelCase to remain consistent with C# naming conventions.
- **Minimal Comments:** Code should be self-explanatory but include comments where complex logic or decisions are implemented.

## XML Integration
In RimWorld mods, XML files are commonly used to define game data that plugins interact with. While this summary does not include XML details, ensure you:
- **Define New Defs:** Use XML to define new DefModExtension data (`TurretPosOffset` is an example) that integrates with the C# logic.
- **Harmonize Data and Code:** XML definitions should complement and support the C# logic, ensuring seamless integration between in-game data representations and functionality.

## Harmony Patching
Harmony is essential to modify the existing game functionality without altering the original codebase.
- **Patch Entry Points:** Consider scenarios where your code interacts with the base game's code. Use Harmony to patch methods at these entry points.
- **Prefix/Postfix Techniques:** Use Harmony's Prefix and Postfix methods to insert or append behavior.
  
## Suggestions for Copilot
- **Helper Methods:** Consider using Copilot to generate helper methods for repetitive logic or to facilitate complex calculations.
- **Code Completion:** Take advantage of Copilot’s code completion to expedite the development of standard logic patterns, such as target selection and attack sequences.
- **Refactoring Assistance:** Use Copilot suggestions to identify redundant code segments that could be consolidated for efficiency.
- **Generating Tests:** Although this summary does not include test files, use Copilot for drafting potential test cases for functionalities like `ThreatDisabled` or `OrderAttack`.

Ensure that any generated code adheres to the conventions and architecture described in this document for consistency and maintainability.
