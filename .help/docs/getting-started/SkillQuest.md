# Skill Quest – TiXL's interactive tutorial system

## Preface

There are many ways to increase user engagement. More features help, but so do usability and ease of use. The final building block is documentation.

We already have hundreds of hours of YouTube videos and a lot of documentation on this wiki. Some people dislike watching videos, and some dislike reading. That’s not a judgement — it’s simply a fact.

TiXL already used the *HowTo* operator as an introduction to different aspects of the UI and operators, but its content was limited and unstructured. With *Skill Quest* we bring an interactive tutorial to a completely new level.

Before the implementation we looked closely at tutorials from computer games and learning apps like Duolingo. From that we derived the following hypothesis:

1. TiXL’s content is **non-linear**. Most users are interested in only some capabilities (especially in the beginning), and each skill requires only a few other skills.
2. Learning from video tutorials often results in following step-by-step solutions for a single problem instead of understanding the many possible combinations.
3. Learning TiXL is similar to learning a foreign language:

   * The “grammar” defines how to connect different operator types.
   * The “vocabulary” consists of the Lib Operators.
   * The “style” or fluency requires practice.
4. Solving problems is a great way to learn with high memory retention.
5. Feedback should be immediate.
6. Progress should be visible and celebrated.
7. Learning and fluency transition into practice, which remains essential even for experts.

## Overview

After many design iterations and prototypes we settled on the following structure:

* The *Skill Quest* is a separate area available from the project hub. It uses a slightly playful design with some gamification.
* It can be hidden in the Settings window.
* TiXL’s knowledge is structured into a *Skill Map* made of linked *Topics*.
* Learning is structured because Topics are locked until their requirements are completed.
* We don’t care about cheating. You can always skip ahead. There are no scores, par times or leaderboards. Nothing is shared online.
* Each *Topic* consists of a linear series of *Levels*.
* Playing a Level puts TiXL into a special game mode with a fixed layout and some interactions and elements hidden.
* In this game mode you must complete small challenges defined by a visual solution. Your task is to recreate the challenge by combining the provided Operators. It’s essentially a puzzle game.
* Each Level is structured into a small Tour that explains what’s going on.
* Depending on the Topic there are variations of the puzzles: images, values, geometry, points, etc.
* Progress is saved after each level, unlocking more and more Topics. If you complete all Topics, you can probably claim to be an expert.

## Contributing new Topics and Levels

The current skill map features roughly 100 Topics. Each requires between 5 and 15 Levels. Creating this much content is a huge undertaking and can’t be handled by the core developers alone. If you are proficient with TiXL and want to share your experience, contributing new Levels is great. This chapter walks you through the necessary steps.

A video tutorial may follow in the future.

### Setup

Levels are stored as Operators in the *Skills* project that comes bundled with the Editor. This means the project is read-only in the standalone version. However, you can still duplicate existing Skill Levels and submit those.

Contributing single Level symbols is already helpful, but there is an even better way. This requires setting up an IDE and building and running TiXL in debug mode. If you follow the steps outlined here, this should take only a few minutes.

### What makes a Level…

For full integration into the Skill Map a Level needs the following:

* A **Symbol**

  * in the `Skills` project
  * with a **correct namespace** (e.g. `Skills.Math.M01.BasicMath`)
  * of type image (I.e. a Texture2D primary output)
* A **Tour**.
* A namespace connected to a Topic in the *Skill Map Editor*.

The next steps explain each part in detail.

### Creating new Level Symbols

One practical workflow:

1. From the *Project Hub* create or open a personal project (for example, “Playground”).
2. Open your project.
3. Open the *Symbol Library* and navigate to the *Skills* project folders.
4. Find an existing Level and drop it onto the canvas.

![alt text](/images/clone-tixl.gif)

5. Select the Level and choose *Symbol Definition* → *Duplicate as new Type* from the graph’s context menu.
6. Pick a new namespace in the `Skills` project. Using an existing one is fine for now.
7. Choose a new name following this pattern:

```
/-----"M": cluster prefix, in this case “M” for Math  
|
| /---"01": a two-digit Topic index grouping all Levels  
| |     in the current namespace (I.e. Skills.Math.M01.BasicMath)
| |
| |/--"a": a short index within the Topic
| ||
| || /-"SomeName"
| || |
M01a_SomeName
```

This doesn’t need to be perfect and can always be adjusted. The main sorting criterion is the namespace; within a Topic the Levels are sorted by symbol name.

8. Open your copied Level.
9. Although you can remove everything, keeping the *Solution* Section, the `[...Quiz]` operator, and the Symbol Output is convenient.
10. Create your challenge.
11. When finished, open the Solution and duplicate your puzzle there.
12. Connect the Solution to the Quiz’s second input.
13. Close the Solution.
14. The Skill Map uses the first link in the Symbol’s description as the Level title. Select the “?” in the *Parameter window* to switch to documentation mode and add a description.

### Creating Tour Points

1. From the App Menu open *TiXL* → *Development Tools* → *Tour Points Editor*.
2. Dock this panel if you prefer.
3. Remove existing Tour Points (note: the editor does not support undo/redo).
4. The *Add Tour Point* button is disabled until an Operator is selected in the graph.
5. Styles:

   * **Comment** – default, closes with “Continue”.
   * **TourPoint** – shows an indicator at the referenced operator.
   * **Tip** – not implemented yet but intended for hints if a user gets stuck.
6. Click *Add description* to enter your text. The editor does not auto-wrap yet.
7. Use the + icons to insert Tour Points.
8. Drag the index number to reorder them.

### Style conventions

There are no fixed rules yet, but consistency matters. These points help:

* Avoid mentioning yourself. Prefer formulations like “TiXL offers…”.
* Keep sentences short and clear. Avoid complex grammar. If unsure, rephrase and simplify. Don’t expect prior knowledge beyond required Skill Quest Topics.
* Aim for 1–5 sentences per Tour Point. Follow existing examples if unsure.
* Consider splitting long Tours into multiple Levels.

### Edit the Skill Map

Open the Editor via *TiXL* → *Development Tools* → *Skill Map Editor*.

It’s an internal tool and less polished than other areas of TiXL. It does not support undo/redo. It lets you create and position *Topics* on a hexagonal canvas. These Topics can then be selected to:

* Assign a title
* Assign a category (e.g. Images or Command)

When you select a Topic, a small “output” icon appears at the top left of the sidebar. Clicking it enables the *Link unlocked topics* mode. When active, selecting a Topic on the map adds or removes it from the current Topic’s unlock targets.

In the sidebar you can also define whether the user needs to complete none, one, or all linked Topics before unlocking another Topic.

## Outlook

In the future some Topics will not focus on replicating an image but will challenge the user with creative exercises, for example exploring interesting combinations within a set of operators.
