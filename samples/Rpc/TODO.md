# About this file

This file has a list of tasks for Claude to do.

## Instructions for Claude

1. Look at the Tasks section below and take a few tasks from the list and do them in a way that is most sensible (whether it is taking an entire section of related tasks, or just one at a time)
2. After they are done, append to DONE.md a summary of what you did, and any future considerations.
3. If you got stuck, append to STUCK.md, with a description of what human intervention is required to proceed

# Tasks

## New entity type: Factory

- I would like a new type of enemy: factory.  Factory is an immobile building with 500 hp.
- I would like a random number of factories in each zone: between 0 and 3 (inclusive).
- When enemies spawn in a zone, I would like them to spawn from factory.  If the factory in a zone is missing, I would like that enemy to spawn from another zone that does have a factory.
- If there are no factories, there are no more enemies spawning.

## New UI feature: minimap

I would like to add a minimap in the common game info area that is common to both Phaser and Canvas modes.
- I only want this minimap to show numeric stats on each zone in the whole world.
- I would like the minimap to have a big brown number to signify how many factories there are in that zone.
- I would like the minimap to have a smaller number that is a semi-bright red to indicate how many enemies are in each zone.

## Scout tweaks and fixes

- Please lower the hp of Scout to 200.
- I am not sure the Scout is actually alerting and drawing any entities to the zone the player is in.

## Scout Wifi indicator tweaks

Please change the wifi style indicator on the Scout as follows:
- While the Scout is getting ready to alert neighboring zones, it should indicate to the user the progress:
- When it starts getting ready, it should be gray, and show only one bar or one wave from the wifi icon
- Then after a second or so, it should show 2 gray bars
- Then 3 gray bars
- Then it can be colored green and flashing while it is actively alerting nearby zones.
