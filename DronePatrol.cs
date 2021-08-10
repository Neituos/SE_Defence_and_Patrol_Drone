﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
		// Keys for configuration options
		const string strBlockTagConfig = "patrol_tag";
		const string strControllerTagConfig = "controller_tag";
		const string strHomeTag = "home_gps";
		string strBlockTag = "[DronePatrol]";
		string strBlockControllerTag = "[DPController]";
		bool _patrolEnabled = true;

		//Configuration Section 
		const double maxFollowRange = 5000; //Distance from home or last location before returning 
		const double maxEnemyRange = 5000; //Max range enemy gets from the drone before returning 
		const float turretRange = 600; //Turret Range 
		const float distanceBuffer = 5; //Used for home and last location. Distace from waypoint before it toggles done (meters)
		const double trackingDistance = 400;  //Distance drone will keep from target
		const bool autoHome = true; //If true, will auto home if not near home when in Idle mode

		//Do not edit below this point.

		//Future expansions
		const string strCamera = "RangeFinder"; //Rangefinder Camera (Optional)
		const string strModeIndicator = "INDICATOR";  //Interior Light - Mode Indicator (Optional) 

		//Pre Defined Global Variables 
		IMyTextPanel _lcdBlock = null;
		IMySensorBlock _sensorBlock = null;
		IMyInteriorLight modeIndicator = null;
		IMyRemoteControl _remoteBlock = null;
		IMyRemoteControl _controllerBlock = null;
		IMyShipConnector _connectorBlock = null;

		Nullable<MyDetectedEntityInfo> targetGrid = null;

		Vector3D lastLoc = new Vector3D(Vector3D.Zero);
		Vector3D _homeLoc = new Vector3D(Vector3D.Zero);

		List<MyWaypointInfo> _patrolRoute = null;

		enum OperationMode
		{
			Stationary,
			SingleCtrl,
			RemoteCtrl
		}
		OperationMode _opMode { get; set; }

		List<IMyUserControllableGun> _activeTurrets = null;

		public string SanitizeCustom(string key, string data) {	return data.Replace(key, "").Replace("=", "").Trim(); }

		Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
		MyCommandLine _commandLine = new MyCommandLine();


		public Program()
        {
			_commands["home"] = UpdateHome;
			_commands["reset"] = Reset;
			_commands["route"] = SetRoute;
			_commands["patrol"] = TogglePatrol;
			_commands["init"] = Initialize;

			var customData = Me.CustomData;
			if(!String.IsNullOrEmpty(customData))
            {
				foreach (var cdata in customData.Split())
				{
					if (cdata.Contains(strBlockTagConfig))
					{
						strBlockTag = SanitizeCustom(strBlockTagConfig, cdata);
					}
					else if (cdata.Contains(strControllerTagConfig))
					{
						strBlockControllerTag = SanitizeCustom(strControllerTagConfig, cdata);
					}
					else if (cdata.Contains(strHomeTag))
                    {
						var homeValue = cdata.Replace(strHomeTag, "").Replace("=", "").Trim();
						//String value is stored as "{X: {0} Y: {1} Z: {2}}"
						Vector3D.TryParse(homeValue, out _homeLoc);
					}
				}
			}
			_opMode = OperationMode.Stationary;

			Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update100;
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }


        //Outputs the status text to the LCD
        void EchoToLCD(string text, bool debug=false, bool append=true)
        {
            if (_lcdBlock != null) _lcdBlock.WriteText(text, append);
            if (debug || _lcdBlock == null) Echo(text);
        }

		public bool AllBlocksFound()
        {
			return _sensorBlock != null && _connectorBlock != null && _remoteBlock != null;
		}

		public int ScanForBlocks(bool rescan=false)
        {
			if (!AllBlocksFound() || rescan)
			{
				EchoToLCD("Scanning for available blocks...");

				List<IMyTerminalBlock> searchResults = new List<IMyTerminalBlock>();
				GridTerminalSystem.SearchBlocksOfName(strBlockTag, searchResults, x => Me.CubeGrid == x.CubeGrid);

				foreach (var block in searchResults)
				{
					if (block is IMySensorBlock) _sensorBlock = (IMySensorBlock)block;
					else if (block is IMyTextPanel) _lcdBlock = (IMyTextPanel)block;
					else if (block is IMyShipConnector) _connectorBlock = (IMyShipConnector)block;
					else if (block is IMyRemoteControl)
					{
						if (block.CustomName.Contains(strBlockControllerTag))
						{
							_controllerBlock = (IMyRemoteControl)block;
						}
						else
							_remoteBlock = (IMyRemoteControl)block;
					}

					if (AllBlocksFound()) break;
				}

				if (AllBlocksFound())
                {
					if (_activeTurrets == null)
					{
						_activeTurrets = new List<IMyUserControllableGun>();
						GridTerminalSystem.GetBlocksOfType<IMyUserControllableGun>(_activeTurrets, x => Me.CubeGrid == x.CubeGrid);
					}

					//Make sure the remote block assumes patrol by default
					if (rescan) _remoteBlock.FlightMode = FlightMode.Patrol;
					return 0;
				}

				if (_sensorBlock == null) EchoMissing("sensor");
				if (_connectorBlock == null) EchoMissing("connector");
				if (_remoteBlock == null) EchoMissing("remote controller");

				return -2;
			}
			return 0;
		}

		public void EchoMissing(string blocktype) {	EchoToLCD($"No {blocktype} block found with defined '{strBlockTag}' tag."); }

		public void SaveHomeCoordinates(string coords) { if (_connectorBlock != null) _connectorBlock.CustomData = coords;	}

		public void Reset()
        {
			EchoToLCD("Resetting configuration information.");
			_patrolRoute = null;
			_homeLoc = new Vector3D(Vector3D.Zero);
			Initialize(echo:false);
		}

		public void UpdateHome() { SaveHomeCoordinates(Me.GetPosition().ToString()); }

		public void SetRoute() { CloneWaypoints(); }

		public void Initialize() { Initialize(true); }
		public void Initialize(bool echo)
        {
			if(echo) EchoToLCD("Initializing Drone Patrol information.");
			_homeLoc = Me.GetPosition();
			ScanForBlocks(true);
			if (_patrolRoute == null)
			{
				CloneWaypoints();
			}
		}

		public void TogglePatrol()
        {
			bool enable = _commandLine.Argument(1).ToLower() == "enable";
			_patrolEnabled = enable;
			if (_remoteBlock != null && !_patrolEnabled) _remoteBlock.FlightMode = FlightMode.OneWay;
		}

        public void Main(string argument, UpdateType updateSource)
        {
			//errorStatus = -2 Block(s) not found
			//errorStatus = -1 Dead Stick Mode (Do nothing)
			//errorStatus =  0 All good
			//errorStatus =  1 Damaged, but can fly
			int errorStatus = 0;

			//Checks for arguments passed into the block.  
			if (_commandLine.TryParse(argument))
            {
				string command = _commandLine.Argument(0);
				if (command != null)
                {
					Action commandAction;
					if (_commands.TryGetValue(command, out commandAction))
						commandAction();
					else
						EchoToLCD($"Unknown command {command}");
                }
            }

			if(!AllBlocksFound())
            {
				ScanForBlocks(true);
				if (!AllBlocksFound()) return;
			}

			EchoToLCD($"Running in {_opMode} mode.", append:false);
			//Sets mod indicator light
			// modeIndicator = (IMyInteriorLight)GridTerminalSystem.GetBlockWithName(strModeIndicator);

			//Checks for the Sensor 
			if (!_sensorBlock.IsFunctional)
			{ 
				EchoToLCD("Sensor is not functioning");
				errorStatus = 1; //Set Damaged State
			}

			//If _active_activeTurrets are destroyed, return back home
			if( _activeTurrets == null || _activeTurrets.Count == 0)
				EchoToLCD("No turrets detected... using scanning mode.");
			else if (_activeTurrets.All(x => !x.IsFunctional))
			{
				EchoToLCD("All turrets damaged or missing");
				errorStatus = 1; //Set Damaged State
			}
			else
			{
				//Ammo Check
				bool hasAmmo = _activeTurrets.Any(turret => turret.HasInventory && turret.GetInventory(0).IsItemAt(0));
				//If no Ammo then return home
				if (!hasAmmo)
				{
					errorStatus = 1;
					EchoToLCD("Turrets out of ammo.");
				}
			}

			//If No Errors then run main AI
			if (errorStatus == 0)
			{
				//Makes sure the Drone does not wander too far
				bool tooFar = false;
				if (Vector3D.DistanceSquared(Me.GetPosition(), _homeLoc) > maxFollowRange * maxFollowRange)
				{
					EchoToLCD("Max range exceeded, returning to default");
					if (!lastLoc.IsZero())
						ReturnLastLoc();
					else
						Patrol();

					tooFar = true;
				}

				//Main AI Section
				//Look for targets
				if (Scan() && !tooFar)
				{
					AttackTarget(targetGrid.Value);
				}
				else //No Targets
				{
					//If Patrole system Enabled
					if (_patrolEnabled)
					{
						if (!ReturnLastLoc()) //If no longer going to last location
							Patrol(); //Run patrole
					}
					else
					{
						if (!ReturnHome()) //If no longer returning home then go idle
							Idle();
					}
				}
				//End AI Section
			}
			else
			{
				MissingOrDamaged();
			}
		}

		void CloneWaypoints()
        {
			if (_patrolEnabled)
			{
				if (_patrolRoute == null)
				{
					_patrolRoute = new List<MyWaypointInfo>();
					if (_controllerBlock == null)
						_remoteBlock.GetWaypointInfo(_patrolRoute);
					else
						_controllerBlock.GetWaypointInfo(_patrolRoute);
				}

				if (_controllerBlock != null)
				{
					_remoteBlock.ClearWaypoints();
					foreach (var item in _patrolRoute) _remoteBlock.AddWaypoint(item);
				}

				if (_controllerBlock != null) _opMode = OperationMode.RemoteCtrl;
				else _opMode = OperationMode.SingleCtrl;
			}
			else _opMode = OperationMode.Stationary;
        }

		//Idle Drone function
		void Idle()
		{
			//If Auto Home enabled then return home if not within buffer range.
			if (Vector3.DistanceSquared(_homeLoc, Me.GetPosition()) > distanceBuffer * distanceBuffer && autoHome)
			{
				ReturnHome();
				return;
			}
			else
			{
				if (modeIndicator != null)
					modeIndicator.SetValue<Color>("Color", new Color(1f, 1f, 0f));

				EchoToLCD("Status: Idle");
				if (_patrolEnabled && !MissingOrDamaged()) Patrol();
				else
				{
					StopPatrol();
					ResetTargets(); //resets any target info

					if (_remoteBlock != null)
					{
						_remoteBlock.SetAutoPilotEnabled(false);
						_remoteBlock.FlightMode = FlightMode.OneWay;
						_remoteBlock.ClearWaypoints();
					}
				}
			}
		}

		void ToggleConnector(bool connect=true)
		{
			if(_connectorBlock != null)
            {
				if (connect) _connectorBlock.Connect();
				else _connectorBlock.Disconnect();
            }
		}

		//If part is missing or damaged
		bool MissingOrDamaged()
		{
			if (_remoteBlock.IsFunctional && _sensorBlock.IsFunctional) return false;
			EchoToLCD("--[[Damaged State]]--");
			ReturnHome();
			return true;
		}

		//Sets the drone to use the patrol RC block
		void Patrol()
		{
			ToggleConnector(false);
			if (_patrolEnabled && _remoteBlock != null)
			{
				if (modeIndicator != null)
					modeIndicator.SetValue<Color>("Color", new Color(0f, 0.5f, 1f));

				ResetTargets(); //resets any target info

				EchoToLCD("Status: Patrol");

				_remoteBlock.SetAutoPilotEnabled(true);
				_remoteBlock.FlightMode = FlightMode.Patrol;
			}
			else Idle();
		}

		//If Patrol RC then it will disable autopilot when called 
		void StopPatrol()
		{
			if (_patrolEnabled)
			{
				if (_remoteBlock != null) _remoteBlock.FlightMode = FlightMode.OneWay;

				if (lastLoc.IsZero()) lastLoc = Me.GetPosition();
			}
		}

		//returns drone to its home location
		private bool ReturnHome()
		{
			//if the drone is within the buffer distance from the home location, try locking the connector
			if (Vector3.DistanceSquared(_homeLoc, Me.GetPosition()) < distanceBuffer * distanceBuffer)
			{
				ToggleConnector();
				return false;
			}

			//indicator light
			if (modeIndicator != null)
				modeIndicator.SetValue<Color>("Color", new Color(0f, 1f, 0f));

			EchoToLCD("Status: Returning Home");
			EchoToLCD("Distance Home: " + Vector3.Distance(_homeLoc, Me.GetPosition()).ToString());

			ResetTargets(); //resets any target info
			StopPatrol(); //In case patrole is still active

			SetWaypoint("Origin", _homeLoc);

			return true;
		}

		//Returns Drone to the last location
		private bool ReturnLastLoc()
		{
			//Checks to make sure the lastLoc was not cleared.
			if (lastLoc.IsZero())
				return false;

			//if the drone is within the buffer distance from its last location
			if (Vector3.DistanceSquared(lastLoc, Me.GetPosition()) < distanceBuffer * distanceBuffer)
			{
				lastLoc = new Vector3D(Vector3D.Zero);
				return false;
			}

			if (modeIndicator != null)
				modeIndicator.SetValue<Color>("Color", new Color(0.5f, 1f, 0.5f));

			EchoToLCD("CK" + lastLoc.IsZero().ToString());
			EchoToLCD("Status: Returning Last Location");

			ResetTargets(); //resets any target info

			SetWaypoint("Last Location", lastLoc);

			return true;
		}

		//follow and attack given target
		void AttackTarget(MyDetectedEntityInfo grid)
		{
			if (modeIndicator != null)
				modeIndicator.SetValue<Color>("Color", new Color(1f, 0f, 0f));

			StopPatrol(); //Stops the patrole RC Block
			EchoToLCD($"Status: Attacking Target {grid.Name} [{grid.Relationship}]");
			ToggleTurrets(); // Make sure turrets are online

			//Gets the offset waypoint.
			Vector3D newPosition = OffsetPos(Me.GetPosition(), grid.Position, trackingDistance);
			SetWaypoint("Target", newPosition);
		}

		//Scans for targets
		private bool Scan()
		{
			EchoToLCD("Scanning for targets");
			if (_sensorBlock.IsActive) //If sensor picks up any targets
			{
				List<MyDetectedEntityInfo> targetList = new List<MyDetectedEntityInfo>();
				_sensorBlock.DetectedEntities(targetList);
				EchoToLCD("Targets found by Sensor: " + targetList.Count.ToString());

				double detectedGridDist = -1;
				double distHolder;
				MyDetectedEntityInfo gridHolder = new MyDetectedEntityInfo();
				bool foundOne = false;

				//Finds the closest target
				for (int i = 0; i < targetList.Count; i += 1)
				{
					if (ValidTarget(targetList[i]))
					{
						distHolder = Vector3D.DistanceSquared(targetList[i].Position, Me.GetPosition());
						if (detectedGridDist < 0 || distHolder < detectedGridDist)
						{
							detectedGridDist = distHolder;
							gridHolder = targetList[i];
							foundOne = true;
						}
					}
				}

				//if target found then set target grid
				if (foundOne)
				{
					EchoToLCD("Target Locked");
					targetGrid = gridHolder;
					return true;
				}
				else EchoToLCD("No valid targets found.");
			}
			else EchoToLCD("Sensor block disabled");

			return false;
		}

		//Resets target var and disables turrets
		void ResetTargets()
		{
			targetGrid = null;
			ToggleTurrets(false); //Disables all turrets
		}

		//Makes sure the target is still valid 
		private bool ValidTarget(MyDetectedEntityInfo grid)
		{
			if (grid.IsEmpty())
				return false;

			//Range Checks 
			if (Vector3D.DistanceSquared(Me.GetPosition(), grid.Position) > maxEnemyRange * maxEnemyRange)
				return false;

			if (grid.Relationship != VRage.Game.MyRelationsBetweenPlayerAndBlock.Enemies)
				return false;

			return true;
		}

		//Calculates a Vector3D point before point B
		private Vector3D OffsetPos(Vector3D a, Vector3D b, double offset)
		{
			Vector3D newPos = -offset * Vector3D.Normalize(b - a) + b;

			return newPos;
		}

		//Sets the waypoint for the main RC Block
		int SetWaypoint(string name, Vector3D pos)
		{
			if (_remoteBlock != null)
			{
				_remoteBlock.ClearWaypoints();
				_remoteBlock.AddWaypoint(pos, name);	

				_remoteBlock.SetAutoPilotEnabled(true);
				_remoteBlock.FlightMode = FlightMode.OneWay;

				EchoToLCD("Distance to target: " + Vector3.Distance(pos, Me.GetPosition()).ToString());
				return 0;
			}
			else
				return -1;
		}

		void ToggleTurrets(bool enabled = true)
        {
			if (_activeTurrets != null) foreach (var turret in _activeTurrets) turret.Enabled = enabled;
		}
	}
}
