# Space Engineers Defence and Patrol Drone (extended)

** Release Notes v0.1 **
Feb-01-2021
- First pass of an updated form of the patrol drone
- Simplified the patrol route usage through a common controller block that can contain multiple waypoints
- Leverage the remote controller's flight mode to indicate different states
- Support custom naming/labelling using block tags (appending [DronePatrol] to a block name)
- Custom Data will allow for overriding the tags used for tags and home location

**General Features:**
- Enemy Auto Target after detection
- Automatically flys to enemy targets to engage them
- Will return home after turret is destroid
- Ability to interface with second Remote Control Block for patrol routs
- Will automatically break patrol to engage enemy
- Will return to last point in patrol to continue patrol
- Will return if exceeds max range
- Will return if runs out of ammo

The Drone's targeting range is only limited by the range of the sensor.
Would suggest using Super Sensor: http://steamcommunity.com/sharedfiles/filedetails/?id=504736273

**How to use**
- Place code in programing block 
- Name the sensor, SENSOR 
- Set Sensor to "Detect Enemy"
- Create at least one Remote Control block
- Add 1 or more turrets to the drone 
- Make sure turret is set to "Target Neutrals" <-- Investigate if this is necessary....

Patrol route is controlled through a second Remote Control block (ideally at the home location)
 - Controller needs to contain the controller tag [DPController]
 - These waypoints will be used as navigational waypoints.

*** Run Arguments ***
 - reset : reset the home location to the current location and execute a rescan of blocks.
 - set_home : set the home location to the current location
 - route : clone the waypoints from the DPController

*** Custom Options ***
 - patrol_tag = STRING_FOR_DETECTION (default: [DronePatrol])
 - controller_tag = STRING_FOR_WAYPOINT BLOCK DISCOVERY (default: [DPController])
 - home_gps = {X: XCOORD Y: YCOORD Z:ZCOORD} (default: current location)

**Future Development:**
- Leveraging a camera for raycast tracking of targets

Indicator light and LCD panel are optional.
- Indicator light will need to be named INDICATOR
- LCD Panel will need to be named LCD Panel

** Original Developer links **
Steam Workshop: http://steamcommunity.com/sharedfiles/filedetails/?id=524850237
github: https://github.com/eldarstorm/SE_Defence_and_Patrol_Drone

![alt tag](http://postnukerp.com/images/RC-Logo_small1a.png)