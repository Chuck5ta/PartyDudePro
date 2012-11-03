using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Markup;

using Zeta;
using Zeta.Common;
using Zeta.TreeSharp;
using Zeta.Common.Plugins;
using Zeta.CommonBot.Profile;
using Zeta.CommonBot.Profile.Common;
using Zeta.CommonBot;
using Zeta.XmlEngine;
using Action = Zeta.TreeSharp.Action;
using Zeta.CommonBot.Settings;
using Zeta.Internals.Actors;
using Zeta.Internals.SNO;


/*
	PartyDudePro is to be used in conjuction with PartyLeaderPro, and enables your bots to run together in a group, and
	with the built-in comms system it also allows them to communicate with each other to work as a team.
	
	When the PartyLeaderPro starts a new game it send out Invite to Party and the PartyDudePro accepts these, and updates
	the comms system in order to inform the leader that they have successfully joined the party.
	
	As the leader send its location to the followers, the PartyDudePro takes these coordinates and moves the character to 
	them, thus resulting in the character following the leader.
	
	If the follower dies, or loses the leader, PartyDudePro will get the character the to TP back to town, the locate
	and use the leader's banner in order to rejoin the party	
	
	Plugins that have been of use in the creation of this plugin:
	- MyBuddy.Local aka Follow me - Author: xsol
	- GilesCombatReplacer - Author: GilesSmith
	- JoinMe! - Author readonlyp	
	
	Author: ChuckyEgg (CIGGARC Developer)
	Support: CIGGARC team, et al
	Date: 3rd of November, 2012
	Verion: 2.0.0
	
 */
namespace PartyDudePro
{
    public class PartyDudePro : IPlugin
    {
		// used to generate a random value to represent the time the char will stop for a spliff, and for how long the char will take to smoke it
		private Random randomNumber = new Random();
		private int randomTime = 0;
		private int randomSeconds = 0;
		private int randomTenthsOfSeconds = 0;
		
		// tables/files that make up the database
		private string commsFolderPath = @"..\";
		private string commsFolderName = "CommsCentre";
		private string commsDirectory = @"..\CommsCentre";
		
		// this is used to ensure that the toon pauses for a random amount of time when in town
		private bool notInParty = true;

        // Set up button object to represent the buttons in the game window
		// this can be used for any button, but only one button at a time
        private Zeta.Internals.UIElement Button = null;	
		
		// Comms 
		private PartyDudeComms dudeRadio = new PartyDudeComms();
		
		// this is a unique ID, that is used to identify it in the party, for communication between
		// party members
		private string partyID = "";
		// this represents what is currently happening in the game. Specifically what the leader is doing 
		//(CreateGame, Running, Stashing)
		private string currentGameState = "CreateParty";
		
		// this is used for creating a gap between the times the party dudes/members
		// read the leader's position (X, Y, Z coordinates) from the PathCoordinates file
        private DateTime LastCoordAcquiredTime { get; set; }
		
		// this holds the X, Y, and Z coordinates of the leader's last known position
		private Vector3 leaderLastPosition;
		
		// Boss encounter
		private bool BossEncounter = false;
		
		// these store information of the leader's location
		private int leaderWorldID = 0;
		private int leaderLevelAreaID = 0;

		// Config window variables/identifiers/objects
		// -------------------------------------------
		// Config window controls
		private TextBox txtCommsSystemFolderPath, txtCommsSystemFolderName;
		private Button btnDone, btnCheckPartyIntegrity;
		private Label lblMessage;
		// The config window object
        private Window configWindow;
		// this holds the location of the config window's XAML file
		private string partyDudeXamlFile = @"Plugins\PartyDudePro\PartyDudePro.xaml";
		

        // DO NOT EDIT BELOW THIS LINE IF YOU DO NOT KNOW WHAT YOU ARE DOING
        // -----------------------------------------------------------------
        // To those who do, feel free :)
        #region Iplugin

        public void Log(string message, LogLevel level = LogLevel.Normal) 
		{ 
			Logging.Write(level, string.Format("{0}", message)); 
		}
		
        public bool Equals(IPlugin other)
        {
            return other.Name == Name;
        }

        public string Author
        {
            get { return "ChuckyEgg"; }
        }

        public string Description
        {
            get { return "Kicking butt in a team of bots!"; }
        }

        public string Name
        {
            get { return "PartyDudePro"; }
        }

        public Version Version
        {
            get { return new Version(2, 0, 0); }
        }

        /// <summary> Executes the shutdown action. This is called when the bot is shutting down. (Not when Stop() is called) </summary>
        public void OnShutdown()
        {
        }		
		
        public void OnEnabled()
        {
			// for when the bot is started or stopped
			Zeta.CommonBot.BotMain.OnStart += BotStarted;
			Zeta.CommonBot.BotMain.OnStop += BotStopped;
			Initialise_All();
			// load settings from the config file
			LoadConfigurationFile();
			//create GameChanged instance
            Zeta.CommonBot.GameEvents.OnGameChanged += GameChanged;
            Log("Bot away, dude!");
        }

        /// <summary> Executes the disabled action. This is called when he user has disabled this specific plugin via the GUI. </summary>
        public void OnDisabled()
        {		
			// for when the bot is started or stopped
			Zeta.CommonBot.BotMain.OnStart -= BotStarted;
			Zeta.CommonBot.BotMain.OnStop -= BotStopped;		
            Zeta.CommonBot.GameEvents.OnGameChanged -= GameChanged;
            Log("Plugin disabled!");
        }

        public void OnInitialize()
        {
			// set up new values for ActualBottingPeriod and ActualSpliffTime, store the current time (representing the time the farming starts)
			Initialise_All();
        }
		

        #endregion

        public void OnPulse()
        {
			
            // Plugin Will Crash D3 if not in game world
			// do not do anything IF
			// - we are not in the game
			// - we are still loading the world
			// - comms directory exists
			if (ZetaDia.IsInGame && !ZetaDia.IsLoadingWorld && Directory.Exists(commsDirectory))
			{
				Zeta.CommonBot.LootTargeting.Instance.Pulse();
		
				// get the current GameState
				currentGameState = dudeRadio.getGameState();
				
				// keep on checking if the leader has finished with the boss and moved on
				if (BossEncounter && currentGameState != "BossEncounter")
				{
					Log("Boss fight is over, now where is the leader?");
					BossEncounter = false;
				}
				
				// are we creating the party - start of a game
				if (currentGameState == "CreateParty")
					checkForPartyInvite();
				else
				{
					// Are we on the run
					if (!inCombat() && !isLooting() && !isStashing() && (currentGameState == "Running" || currentGameState == "BossEncounter"))
						onTheRun();		
					
					// grab Dude's current state
					// PartyID will be = Dude1, Dude2, or Dude3
			//		string currentDudeState = dudeRadio.getDudeState();
					// ready for when we add code that requires the Dude's state
				}

				// OK BUTTON
				// this pops up for a number of reasons:
				// - Portal Stone use
				if (Zeta.Internals.UIElement.IsValidElement(0x891D21408238D18E) && (Button = Zeta.Internals.UIElement.FromHash(0x891D21408238D18E)) != null)
				{
					if (Button.IsVisible)
					{
						Log("Clicking on OK button");
						// click on the ACCEPT button
						Button.Click();
						pauseForABit(2, 3);
					}
				}
				
				// Boss Encounter
				// This clicks on the ACCEPT button of the boss encounter
				// found to be the same hash for all bosses 
				// Skeleton King - The Butcher - Zultan Kull - Belial - Ghom - Azmodan - Diablo
				if (Zeta.Internals.UIElement.IsValidElement(0x69B3F61C0F8490B0) && (Button = Zeta.Internals.UIElement.FromHash(0x69B3F61C0F8490B0)) != null)
				{
					if (Button.IsVisible)
					{
						pauseForABit(1, 3);
						// click on the ACCEPT button
						Button.Click();
						// set the boss encounter to true, so that we don't test for out of Range of the Leader
						BossEncounter = true;
						// Allow time to enter boss area
						while (!inBossArea())
						{
							Log("We are transitioning to boss area");
							pauseForABit(3, 4);
						}
						Log("Time to kick some Boss butt!");
					}
				}
				if (Zeta.Internals.UIElement.IsValidElement(0xF495983BA9BE450F) && (Button = Zeta.Internals.UIElement.FromHash(0xF495983BA9BE450F)) != null)
				{
					if (Button.IsVisible)
					{
						pauseForABit(1, 3);
						// click on the ACCEPT button
						Button.Click();
						// set the boss encounter to true, so that we don't test for out of Range of the Leader
						BossEncounter = true;
						// Allow time to enter boss area
						while (!inBossArea())
						{
							Log("We are transitioning to boss area");
							pauseForABit(3, 4);
						}
						Log("Time to kick some Boss butt!");
					}
				}
				Zeta.CommonBot.CombatTargeting.Instance.Pulse();
			}
			
			// Dude died?
			// If the Revive at last checkpoint BUTTON pops up, then click on it
			if (Zeta.Internals.UIElement.IsValidElement(0xBFAAF48BA9316742) && (Button = Zeta.Internals.UIElement.FromHash(0xBFAAF48BA9316742)) != null)
			{
				if (Button.IsVisible)
				{
					Log("Revive at last checkpoint BUTTON has popped up");
					// wait for a second or 2
					pauseForABit(1, 2);
					Log("Clicking on the Revive at last checkpoint BUTTON");
					// click on the Revive at last checkpoint BUTTON
					Button.Click();
					// wait for a second or 2
					pauseForABit(1, 2);
				}
			}
			
			
        } // END OF OnPulse()
		
		/*
			This method represent the BotMain event OnStart (Zeta.CommonBot.BotMain.OnStart)
			We need for the party formation to start again
		 */
        public void BotStarted(Zeta.CommonBot.IBot bot)
		{						
			// Reload profile
			Zeta.CommonBot.ProfileManager.Load(GlobalSettings.Instance.LastProfile);				
			pauseForABit(1, 2);
			Initialise_All();
		}		
		
		/*
			This method represent the BotMain event OnStop (Zeta.CommonBot.BotMain.OnStop)
			We need for the comms database to be removed, because on the next 
		 */
        public void BotStopped(Zeta.CommonBot.IBot bot)
		{			
			// Do nothing for now
		}
		
		/*
			this mothod looks for and invite to a party, accepts it, and 
			acquires an ID for the dude
			updates the PartyState file, signifying this dude is in the part
		 */
		private void checkForPartyInvite()
		{			
			Log("We are in Party Creation mode");
            // Wait for invite
            if (Zeta.Internals.UIElement.IsValidElement(0x9EFA05648195042D) && (Button = Zeta.Internals.UIElement.FromHash(0x9EFA05648195042D)) != null)
            {
                // is the invite graphic visible
                if (Button.IsVisible)
                {
                    Log("Invite incoming!");
                    // invite has shown up, accept it
                    pauseForABit(1, 3);
                    Button.Click();

                    // Confirm joining party (click OK)
                    if (Zeta.Internals.UIElement.IsValidElement(0xB4433DA3F648A992) && (Button = Zeta.Internals.UIElement.FromHash(0xB4433DA3F648A992)) != null)
                    {
                        // is the joining party confirmation button visible
                        if (Button.IsVisible)
                        {
                            // invite has shown up, accept it
                            pauseForABit(1, 3);
                            Log("Accepting the invite to the party");
                            // click ok
                            Button.Click();
							// we are now in a party
                        }
                    }
			 
					// retrieve this dude's party member ID
					// required for inter-party communication
					partyID = dudeRadio.retrievePartyMemberID();

					// update PartyState file saying this char is present
					// need to make sure that the PartyState for this characters is set to Present
					while (dudeRadio.thisDudeIsPresent(partyID) == false)
					{
						dudeRadio.updatePartyState("Present");
					}
                }
            }

            // Follower leaving message
            // need to OKAY this in order to get rid of it
            if (Zeta.Internals.UIElement.IsValidElement(0xF85A00117F5902E9) && (Button = Zeta.Internals.UIElement.FromHash(0xF85A00117F5902E9)) != null)
            {
                if (Button.IsVisible)
                {
                    Log("Follower is leaving message has popped up");
                    // click on the OK button
                    Button.Click();
                }
            }
            // Follower is joining up again - if you are the only one in the party, then might as well have the follower rejoin
            // need to OKAY this in order to get rid of it
            if (Zeta.Internals.UIElement.IsValidElement(0x161745BBCE22A8BA) && (Button = Zeta.Internals.UIElement.FromHash(0x161745BBCE22A8BA)) != null)
            {
                if (Button.IsVisible)
                {
                    Log("Follower is joining message has popped up");
                    // click on the YES button
                    Button.Click();
                }
            }
            // Follower is joining up again - if you are the only one in the party, then might as well have the follower rejoin
            // need to OKAY this in order to get rid of it
            if (Zeta.Internals.UIElement.IsValidElement(0x161745BBCE22A8BA) && (Button = Zeta.Internals.UIElement.FromHash(0x161745BBCE22A8BA)) != null)
            {
                if (Button.IsVisible)
                {
                    Log("Follower is joining message has popped up");
                    // click on the YES button
                    Button.Click();
                }
            }
		} // END OF checkForPartyInvite()
		
		/*
			this method deals with what the Dude does while following the leader on
			a run
			it makes sure it stays with the leader
		 */
		private void onTheRun()
		{		
			Zeta.CommonBot.LootTargeting.Instance.Pulse();	
			// only perform the grab coords and MoveTo every few seconds
			// and when NOT in combat or looting
			if(DateTime.Now.Subtract(LastCoordAcquiredTime).TotalMilliseconds > 2000)
			{	
				// AND not in combat and not looting or stashing - hopefully
				if (!isStashing())
				{
					// grab coordinates of leader from PathCoordinates file
					string[] leaderLocationDetails = dudeRadio.getPathCoordinates();
					leaderWorldID = Convert.ToInt32(leaderLocationDetails[0]);
					leaderLevelAreaID = Convert.ToInt32(leaderLocationDetails[1]);
					string[] leaderPathCoords = leaderLocationDetails[2].Split('#');
					// store leaderPathCoords as a Vector3 
					leaderLastPosition = new Vector3(float.Parse(leaderPathCoords[0]), float.Parse(leaderPathCoords[1]), float.Parse(leaderPathCoords[2]));
						
					// this checks to see if the leader's level area is different to the follower's
					// if it is, it will check to see if there is a portal nearby, and if so use it
					// then it can move of to the following code to check on out of range
					leaderLevelAreaChangeCheck(leaderLevelAreaID);
						
					// now we need to make sure the leader hasn't gotten too far away
					// if the leader is too far away, we will need to TP back to town and use the banner to
					// rejoin the main party
						
					// only move if we are beyond a certain distance of the leader
					//  WILL THIS WORK ???  ZetaDia.Me.Position.Distance ?????
					if (!outOfRangeOfLeader())
					{
						// get the char to follow at a random distance (between 10 and 22)
						int allowedDistance = randomNumber.Next(5, 10);
						if (ZetaDia.Me.Position.Distance(leaderLastPosition) > allowedDistance)
						{
							// Move to these coordinates
							moveTo(leaderPathCoords[0], leaderPathCoords[1], leaderPathCoords[2]);
						}
						// will these allow for combat and looting
						Zeta.CommonBot.CombatTargeting.Instance.Pulse();
					}
					else // we are too far away from leader, let's TP and banner
					{
					
						// only check this is we are not in a Boss area
						// checks that both the follower and the leader are not in the boss area
						currentGameState = dudeRadio.getGameState();
						// only TP out if we are safe to do so
						//    and not grabbing some lovely loot ;)
						if(!inCombat() && !isLooting() && !BossEncounter && currentGameState != "BossEncounter")
						{
							// TP back to base							
							Log("I HAVE LOST THE LEADER!!!!");
							// bugger, let's find them!
							catchUpWithTheParty();
						}
					}
				}
			}
		} // END OF onTheRun()	
	
		
        /*
            This method checks to see if the leader is in a different Level Area than the follower.
			If it is, then we need to see if there is a portal nearby, and if so use it.
         */
        private void leaderLevelAreaChangeCheck(int levelAreaID)
        {
			// check if the leader's Level Area ID is different to the follower's
			if (ZetaDia.CurrentLevelAreaId != levelAreaID && !BossEncounter)
			{
				// Is there a portal nearby
				// if so, locate portal and use it				
				
				foreach (Actor worldActor in ZetaDia.Actors.RActorList)
				{
					// in order to be able to use the object's data we need to convert it to a DiaObject
					DiaObject worldObject = (DiaObject)worldActor;

					// is the object a portal, within 10 feet and NOT a BossPortal ?
					if(worldObject is Zeta.Internals.Actors.Gizmos.GizmoPortal && worldObject.Distance < 11)
					{
						Log("There is  portal nearby!");
						ZetaDia.Me.UsePower(SNOPower.Walk, worldObject.Position, ZetaDia.Me.WorldDynamicId, -1);
						BotMain.PauseFor(System.TimeSpan.FromSeconds(randomNumber.Next(2, 3)));
						Log("Current coords: " + worldObject.Position.ToString());
						Log("My position coords: " + ZetaDia.Me.Position.ToString());
						// store the followers current coordinates
						Vector3 prePortalCoordinates = ZetaDia.Me.Position;
						// get me through that portal
						// repeat until our world position has changed
						while(prePortalCoordinates.Distance(ZetaDia.Me.Position) < 5)
						{
							Log("Use dat portal, mon");
							ZetaDia.Me.UsePower(SNOPower.Axe_Operate_Gizmo, worldObject.Position, ZetaDia.Me.WorldDynamicId, worldObject.ACDGuid);
							// pause bot for a bit
							BotMain.PauseFor(System.TimeSpan.FromSeconds(randomNumber.Next(1, 2)));
						}
						// Set this follower's DudeState file to Running, so that the leader knows that 
						// all the followers are through the portal and ready to continue the run again
						dudeRadio.updateDudeState("Running");
						pauseForABit(1,2);
						break;
					}
				}
				
				// No portal
				// nothing more to check on, we may be out of range, so let that code deal with that possibility
				
			}
		}
		
		
        /*
            This method checks to see if we are in combat
			If we are in combat then it returns true, else it returns false
         */
        private bool inCombat()
        {
			if (Zeta.CommonBot.CombatTargeting.Instance.FirstNpc == null && Zeta.CommonBot.CombatTargeting.Instance.FirstObject == null)
			{
				return false;
			}
			else
			{
				return true;
			}
			
        } // END OF inCombat()
		
		
        /*
            This method checks to see if we are in combat
			If we are in combat then it returns true, else it returns false
         */
        private bool inCombat(string message)
        {
			if (Zeta.CommonBot.CombatTargeting.Instance.FirstNpc == null && Zeta.CommonBot.CombatTargeting.Instance.FirstObject == null)
			{
				return false;
			}
			else
			{
                Log("We are unable to " + message + " because we are in combat!");
				return true;
			}
			
        } // END OF inCombat()
		
		
        /*
            This method checks to see if we are picking up loot
			If we are looting then it returns true, else it returns false
         */
        private bool isLooting()
        {
			if (Zeta.CommonBot.LootTargeting.Instance.FirstNpc == null && Zeta.CommonBot.LootTargeting.Instance.FirstObject == null)
			{
				return false;
			}
			else
			{
				return true;
			}
			
        } // END OF isLooting()
		
		
        /*
            This method checks to see if we are stashing our loot
			If we are stashing then it returns true, else it returns false
         */
        private bool isStashing()
        {
			if (Zeta.CommonBot.Logic.BrainBehavior.IsVendoring)
			{
				return true;
			}
			else
			{
				return false;
			}
			
        } // END OF isStashing()
		
        /*
            This method checks to see if we are in a boss area
         */
        private bool inBossArea()
        {
			switch (ZetaDia.CurrentWorldId)
			{
				case 174449: // Cain's House
				case 60713: //  Lerori's Passage
				case 73261: //  Skeleton King
				case 182976: // Spider Queen Aranea
				case 78839: //  The Butcher
				case 195200: // Maghda
				case 81715: //  Visit the prince
				case 60193: //  Zoltun Kulle
				case 60756: //  Belial
				case 103209: // Ghom 
				case 226713: // The Seigebreaker Assault Beast
				case 119650: // Cydaea
				case 121214: // Azmodan
				case 166640: // Rakanoth
				case 103910: // Crystal Colonnade - non TP area
				case 214956: // Izual The Betrayer
				case 205399: // Pinnacle of Heaven
				case 109561: // Diablo
				case 153670: // Shadow Realm
					return true;
				default:
					return false;
			}
			
        } // END OF inBossArea()
		
		
        /*
            This method checks to see if we are a long way from the leader. 
			If we are, we need to catch up by TPing back to town, then use the leader's banner 
			to rejoin the party
         */
        private bool outOfRangeOfLeader()
        {
			// only check this is we are not in a Boss area
			// checks that both the follower and the leader are not in the boss area
			// make sure the leader is not too far away, or in a different world, or that the follower is not dead
			currentGameState = dudeRadio.getGameState();
			if (!BossEncounter && currentGameState != "BossEncounter" && !ZetaDia.Me.IsDead) // need to add && NOT DEAD
			{
				if (ZetaDia.Me.Position.Distance(leaderLastPosition) > 150 || ZetaDia.CurrentWorldId != leaderWorldID)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			else
			{
				Log("Out of Range check disabled - We are in a Boss Encounter!");
			}
			return false; // we are in a Boss encounter, therefore we need for the follower to follow and fight
			
        } // END OF outOfRangeOfLeader()
		
        /*
            This method is called when the leader is found to be too far away from the char (also after follower death).
			Either they literally have moved a long way from the char, or they have ported to another area,
			such as an instance (cave, house, etc.)
         */
        private void catchUpWithTheParty()
        {	
			// TP back to town
			while(!ZetaDia.Me.IsInTown && outOfRangeOfLeader())
			{
				ZetaDia.Me.UseTownPortal();
				pauseForABit(1, 2);
			}
			
			if (ZetaDia.Me.IsInTown && outOfRangeOfLeader())
			{
			
				// update the dude's state to ensure that it is running with the party
				dudeRadio.updateDudeState("Running");
				Log("I'm off to use the leader banner!");
				Log("====================================");
				// locate and use the leader's banner
				IEnumerator<Actor> ied = ZetaDia.Actors.ACDList.GetEnumerator();
				while (ied.MoveNext())
				{
					if (null != ied.Current)
					{
						ACD acdbanner = (ACD)ied.Current;

						if (acdbanner.Name.Contains("Banner_Player_1") && acdbanner.ActorType == Zeta.Internals.SNO.ActorType.Gizmo)
						{
							// move to the leader's banner
							ZetaDia.Me.UsePower(SNOPower.Walk, acdbanner.Position - new Vector3(2, 2, 0), ZetaDia.Me.WorldDynamicId, -1);
							// pause for a bit
							pauseForABit(1, 2);

							// use the banner to rejoin the party
							ZetaDia.Me.UsePower(SNOPower.Axe_Operate_Gizmo, acdbanner.Position, ZetaDia.Me.WorldDynamicId, acdbanner.ACDGuid);

							// pause bot for a bit
							BotMain.PauseFor(System.TimeSpan.FromSeconds(randomNumber.Next(1, 2)));

							if (!ZetaDia.Me.IsInTown) 
								break; //Try Again if Fail
						}
					}
				}
				pauseForABit(1, 3);
			}
			
        } // END OF catchUpWithTheParty()		

        /*
            This is executed whenever a new game is created
        */
        public void GameChanged(object sender, EventArgs e)
        {
            Log("New game!");
			
			LoadConfigurationFile();

			// pause for a few seconds (3 to 6), while leader creates the party
            Log("Wait for the leader to create a new game");
			pauseForABit(3, 6);
		} // END OF GameChanged(object sender, EventArgs e)

		/*
			This method initialises a number of variables to the starting values required when the char starts on a new run after a break or
			when DB is first run
		 */
        private void Initialise_All()
        {		
			// initialisation of variables goes here
			
			// Boss encounter
			BossEncounter = false;
        } // END OF Initialise_All()
		
		/*
			This method takes in a MoveTo request and converts the string coordinate values into floats so that DemonBuddy can process it and make the char move to 
			the required coordinates
		 */
		private void moveTo(string xCoord, string yCoord, string zCoord)
		{
			// generate a random value to represent a slight variation on the leader's actual coordinates
			// so as to not run exactly the same coordinates as the leader
            Vector3 minorCoordinateChange = new Vector3(randomNumber.Next(-3, 3), randomNumber.Next(-3, 3), randomNumber.Next(-3, 3));	
			Vector3 moveToCoordinates = new Vector3(float.Parse(xCoord), float.Parse(yCoord), float.Parse(zCoord));
			moveToCoordinates += minorCoordinateChange;
			// action the move to the new coordinates
			ZetaDia.Me.UsePower(SNOPower.Walk, moveToCoordinates, ZetaDia.Me.WorldDynamicId, -1);
		} // END OF moveTo(string xCoord, string yCoord, string zCoord, string logMessage)
		
		/*
			This method takes in a MoveTo request and converts the string coordinate values into floats so that DemonBuddy can process it and make the char move to 
			the required coordinates
			
			This is an overloaded method. It allows the passing of a text message, which will be displayed on DemonBuddy's running log
		 */
		private void moveTo(string xCoord, string yCoord, string zCoord, string logMessage)
		{
			Log(logMessage);
			// generate a random value to represent a slight variation on the leader's actual coordinates
			// so as to not run exactly the same coordinates as the leader
            Vector3 minorCoordinateChange = new Vector3(randomNumber.Next(-3, 3), randomNumber.Next(-3, 3), randomNumber.Next(-3, 3));	
			Vector3 moveToCoordinates = new Vector3(float.Parse(xCoord), float.Parse(yCoord), float.Parse(zCoord));
			moveToCoordinates += minorCoordinateChange;
			// action the move to the new coordinates
			ZetaDia.Me.UsePower(SNOPower.Walk, moveToCoordinates, ZetaDia.Me.WorldDynamicId, -1);
		} // END OF moveTo(string xCoord, string yCoord, string zCoord, string logMessage)
		
		/*
			This method pauses the bot for a randomly generated amount of time (minutes and seconds)
		 */
		private void pauseForABit(int minValue, int maxValue)
		{
			// wait for a second or 2
			randomSeconds = (randomNumber.Next(minValue, maxValue) * 1000);
			randomTenthsOfSeconds = (randomNumber.Next(1, 59) * 10);
			randomTime = randomSeconds + randomTenthsOfSeconds;
			Thread.Sleep(randomTime);
		} // END OF pauseForABit(int minValue, int maxValue)

	
		
        // ******************************************************
        // *****  Load the Config GUI's configuration file  *****
        // ******************************************************
        private void LoadConfigurationFile()
        {
            // Check that the configuration file exists, if not create one
            if (!File.Exists(@"Plugins\PartyDudePro\ConfigSettings"))
            {
                Log("Configuration file does not exist, we are creating a new one based on the default values: ");
				// create a new config file
                SaveConfigurationFile();
                return;
            }
            // Load the config file
            using (StreamReader configReader = new StreamReader(@"Plugins\PartyDudePro\ConfigSettings"))
            {
				// read in the first line
				string[] config = configReader.ReadLine().Split('=');				
				// COMMS system
				commsFolderPath = @config[1];
				config = configReader.ReadLine().Split('=');
				commsFolderName = config[1];				
				commsDirectory = commsFolderPath + commsFolderName;	
				// send the COMMS path and name to the PartyDudeComms so that it can make use of those values
				dudeRadio.setCommsFolderPathAndName(commsDirectory);
				
                configReader.Close();
            }
        } // END OF LoadConfigurationFile()
		
		// ***********************************************
        // ***** Save the Config GUI's Configuration *****
        // ***********************************************
        private void SaveConfigurationFile()
        {
            FileStream configStream = File.Open(@"Plugins\PartyDudePro\ConfigSettings", FileMode.Create, FileAccess.Write, FileShare.Read);
            using (StreamWriter configWriter = new StreamWriter(configStream))
            {
				// COMMS system
                configWriter.WriteLine("commsFolderPath=" + commsFolderPath);
                configWriter.WriteLine("commsFolderName=" + commsFolderName);
				// store the comms file and path
				commsDirectory = commsFolderPath + commsFolderName;	
				// send the COMMS path and name to the PartyDudeComms so that it can make use of those values
				dudeRadio.setCommsFolderPathAndName(commsDirectory);
				
            }
            configStream.Close();
        } // END OF SaveConfiguration()
	
		
	
        // ********************************************
        // *********** CONFIG WINDOW REGION ***********
        // ********************************************
		// original version by GilesSmith (from GilesCombatReplacer)
		// half inched and reconfigured by ChuckyEgg ;)
        #region configWindow
        public Window DisplayWindow
        {
            get
            {
                if (!File.Exists(partyDudeXamlFile))
                    Log("ERROR: Can't find PartyDudePro.xaml");
                try
                {
                    if (configWindow == null)
                    {
                        configWindow = new Window();
                    }
                    StreamReader xamlStream = new StreamReader(partyDudeXamlFile);
                    DependencyObject xamlContent = XamlReader.Load(xamlStream.BaseStream) as DependencyObject;
                    configWindow.Content = xamlContent;

                    btnDone = LogicalTreeHelper.FindLogicalNode(xamlContent, "btnDone") as Button;
                    btnDone.Click += new RoutedEventHandler(btnDone_Click);		

					// text/info below the Check Party Inegrity combo box
                    lblMessage = LogicalTreeHelper.FindLogicalNode(xamlContent, "lblMessage") as Label;		
					
					// COMMS SYSTEM SET UP
                    txtCommsSystemFolderPath = LogicalTreeHelper.FindLogicalNode(xamlContent, "txtCommsSystemFolderPath") as TextBox;
                    txtCommsSystemFolderPath.TextChanged += txtCommsSystemFolderPath_TextChanged;					
					
                    txtCommsSystemFolderName = LogicalTreeHelper.FindLogicalNode(xamlContent, "txtCommsSystemFolderName") as TextBox;
                    txtCommsSystemFolderName.TextChanged += txtCommsSystemFolderName_TextChanged;
					
                    UserControl mainControl = LogicalTreeHelper.FindLogicalNode(xamlContent, "mainControl") as UserControl;
                    // Set height and width to main window
                    configWindow.Height = mainControl.Height + 30;
                    configWindow.Width = mainControl.Width;
                    configWindow.Title = "Party Leader / Party Dude";

                    // On load example
                    configWindow.Loaded += new RoutedEventHandler(configWindow_Loaded);
                    configWindow.Closed += configWindow_Closed;

                    // Add our content to our main window
                    configWindow.Content = xamlContent;
                }
                catch (XamlParseException ex)
                {
                    // You can get specific error information like LineNumber from the exception
                    Log(ex.ToString());
                }
                catch (Exception ex)
                {
                    // Some other error
                    Log(ex.ToString());
                }
                return configWindow;
            }
        } // END OF Window DisplayWindow
		
		/*
			This method initialises the controls to their default settings when the config window is displayed
		 */
        private void configWindow_Loaded(object sender, RoutedEventArgs e)
        {
			// Load the configuration file for the config window
			LoadConfigurationFile();
			// COMMS system database
			txtCommsSystemFolderPath.Text = commsFolderPath;
			txtCommsSystemFolderName.Text = commsFolderName;
        } // END OF configWindow_Loaded(object sender, RoutedEventArgs e)
		
		/*
			This method closes the config window
		 */
        private void configWindow_Closed(object sender, EventArgs e)
        {
            configWindow = null;
        } // END OF configWindow_Closed(object sender, EventArgs e)
		
		/*
			This method represents the txtCommsSystemFolderPath TextBox TextChanged event 
			this text box holds the path to the comms database folder 
			(it does not include the folder name)
		 */		
        private void txtCommsSystemFolderPath_TextChanged(object sender, RoutedEventArgs e)
        {
			// store contents 
			commsFolderPath = @txtCommsSystemFolderPath.Text;
		} // END OF txtCommsSystemFolderPath_TextChanged(object sender, RoutedEventArgs e)
		
		/*
			This method represents the txtCommsSystemFolderName TextBox TextChanged event 
			this text box holds the name of the comms database's folder 
		 */		
        private void txtCommsSystemFolderName_TextChanged(object sender, RoutedEventArgs e)
        {
			// store contents 
			commsFolderName = txtCommsSystemFolderName.Text;
		} // END OF txtCommsSystemFolderName_TextChanged(object sender, RoutedEventArgs e)
		
		/*
			This method represents the btnDone button event when someone left mouse clicks on the button
			It closes the configuration window
		 */		
        private void btnDone_Click(object sender, RoutedEventArgs e)
        {
			// check that the path exists
            if(Directory.Exists(@txtCommsSystemFolderPath.Text + @txtCommsSystemFolderName.Text)) 
            {	
				lblMessage.Visibility = System.Windows.Visibility.Hidden;		
				// Save the current config window settings to the config file
				SaveConfigurationFile();
				// close the config GUI
				configWindow.Close();
			}
			else
			{
				// invalid path and folder name
				lblMessage.Visibility = System.Windows.Visibility.Visible;
			}
        } // END OF btnDone_Click(object sender, RoutedEventArgs e)
		
	
	
			
        #endregion
        // ***************************************************
        // *********** END OF CONFIG WINDOW REGION ***********
        // ***************************************************
		
    }
	
}
