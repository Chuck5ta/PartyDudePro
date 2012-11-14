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
	PartyLeaderPro is used in conjunction with PartyDudePro, to allow your bots to work in a party and has a comms system to
	allow the bots to communicate with each other, thus allowing to also work as a team
	
	This plugin, PartyLeaderPro used by the character/bot who will be the leader of the party.
	
	At the start of every new game the leader (PartyLeaderPro) will send out Invite to Party to the other characters in its 
	friend's list. It will keep on doing this until all places in the party have been filled.
	
	Once it has been notified by all the party members that they are present int the party, the leader will start the run.
	
	During the run, the leader passes its location in the world (coordinates) to the followers (other party members), thus
	allowing them to follow the same path as the leader.
	
	The config window for the leader, allows for the following to be set:
	1) size of party (total number of members)
	2) whether or not to make regular checks on the party (has anyone left?)
	3) Path and folder name of the COMMS system (database)
	
	Plugins that have been of use in the creation of this plugin:
	- MyBuddy.Local aka Follow me - Author: xsol
	- GilesCombatReplacer - Author: GilesSmith
	- JoinMe! - Author readonlyp		
	
	Author: ChuckyEgg (CIGGARC Developer)
	Support: CIGGARC team, et al, especially Tesslarc ;)
	Date: 14th of November, 2012
	Verion: 2.0.4
	
 */
namespace PartyLeaderPro
{
    public class PartyLeaderPro : IPlugin
    {
		// used to generate a random value to make the action appear more natural
		private Random randomNumber = new Random();
		private int randomTime = 0;
		private int randomSeconds = 0;
		private int randomTenthsOfSeconds = 0;
		
		// tables/files that make up the database
		private string commsFolderPath = @"..\";
		private string commsFolderName = "CommsCentre";
		private string commsDirectory = @"..\CommsCentre";
		
		// this represents what is currently happening in the game. Specifically what the leader is doing 
		//(CreateGame, Running, Stashing)
		private string currentGameState = "CreateParty";
		
        // this represents the total number of people that will make up the party
        // this can be 2, 3 or 4
        private int totalNumberOfPartyMembers = 2;
		
		// this is used for creating a gap between the times the leader
		// writes it position (X, Y, Z coordinates) to the PathCoordinates file
        private DateTime LastPostionCheck;
		
        // These are used for checking if anyone has left the party. Check made every couple of minutes
		// --------------------------------------------------------------------------------------------

        // this is used to allow the bot to check if anyone has left the party
        // if set to true, it will make the check, otherwise it will not
        // if someone has left the party, a new game will be created
        private bool enablePartyCheck = true;
		
		// The time of the last check on the party state (did anyone leave the party)
        private DateTime lastPartyCheckTime;

        // the time gap between checks for party dropouts
        // 2 = 2 minutes
        private int checkOnPartyTimeGap = 2;

        // this is used as a flag to signify if a member of the party has left
        // if someone has left, then we must create a new game
        private bool partyMemberHasLeft = false;

        // This flag is used to test for multiple game creations within a short period of time, just incase someone has
        // left the group and unable to coe back, and nobody is around to alter totalNumberOfPartyMembers to reflect
        // this change in the total number of party members
        private int numberOfGameCreationsInTheLastTenMins = 0;
        private DateTime gameCreationMonitorStartTime;

		// Config window variables/identifiers/objects
		// -------------------------------------------
		// Config window controls
        private RadioButton check2DudeParty, check3DudeParty, check4DudeParty;
        private CheckBox checkParty;
		private ComboBox cmbCheckOnPartyTimeGap;
		private TextBox txtCommsSystemFolderPath, txtCommsSystemFolderName;
		private Label lblComboBoxInfo1, lblComboBoxInfo2, lblComboBoxInfo3, lblMessage;
		private Button btnDone, btnCheckPartyIntegrity, btnLeaderControl;
		// The config window object
        private Window configWindow;
		// this holds the location of the config window's XAML file
		private string partyLeaderProXamlFile = @"Plugins\PartyLeaderPro\PartyLeaderPro.xaml";
		
		
		private bool check2DudePartySetting, check3DudePartySetting, check4DudePartySetting;
		
		// Comms 
		private LeaderComms leaderRadio = new LeaderComms();
		
		// this states how the leader of the party if controlled. Either by the bot or by a human
		private bool leaderControlledByBot = true;
		
		// Boss encounter
		private bool BossEncounter = false;
		

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
            get { return "PartyLeaderPro"; }
        }

        public Version Version
        {
            get { return new Version(2, 0, 4); }
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
			// When activated, this is used to invite the people to the party
            Zeta.CommonBot.GameEvents.OnGameChanged += GameChanged;
			// Not actually used at the moment, but to be made use of.... possibly
            Zeta.CommonBot.GameEvents.OnGameLeft += GameLeft;
			// When the player dies this event deals with what needs to be done
            Zeta.CommonBot.GameEvents.OnPlayerDied += OnPlayerDied;
			// set default values to certain variables, identifiers, objects, etc.
            Initialise_All();
            Logging.Write("Bot away, dude!");
			
			LoadConfigurationFile();
			
			// create the folder that will contain the database
			leaderRadio.createCommsFolder(commsFolderPath + commsFolderName);
			// create the database
			leaderRadio.createCommsDatabase(commsFolderPath, totalNumberOfPartyMembers);
			
			// CREATE THE PARTY
			// ****************
			// Upadate GameState to "CreateParty"
			leaderRadio.updateGameState("CreateParty");
        }

        /// <summary> Executes the disabled action. This is called when he user has disabled this specific plugin via the GUI. </summary>
        public void OnDisabled()
        {
			// for when the bot is started or stopped
			Zeta.CommonBot.BotMain.OnStart -= BotStarted;
			Zeta.CommonBot.BotMain.OnStop -= BotStopped;
            Zeta.CommonBot.GameEvents.OnGameChanged -= GameChanged;
            Zeta.CommonBot.GameEvents.OnGameLeft -= GameLeft;
            Zeta.CommonBot.GameEvents.OnPlayerDied -= OnPlayerDied;
            Log("Plugin disabled!");
			
			// delete the database
			leaderRadio.deleteCommsFolder();
        }

        public void OnInitialize()
        {
        }
		
        #endregion

        public void OnPulse()
        {
			// grab the current game state
			currentGameState = leaderRadio.getGameState();
			
			if (ZetaDia.IsInGame && !ZetaDia.IsLoadingWorld && currentGameState != "Waiting")
			{
			
				// check that the database exists
				// if not then there could be trouble, so we must assume the game and party have not been created properly
				// best way to resolve this, willbe to leave game thus forcing the creation of a new game
				// this is just-in-case the members are already under the impression that they are in the party and
				// therefore the relevant database files will not get updated properly
				try
				{
					if (!Directory.Exists(commsDirectory)) 
					{
						// leave game to create a new one
						Log("PROBLEM! Tried to start a new game, but the database is missing. We are now leaving and will recreate the game and party!");
						pauseForABit(3, 6);
						ZetaDia.Service.Games.LeaveGame();
						pauseForABit(2, 3);
					}
					else
					{
						// boss encounter check
						if (BossEncounter)
						{
							// are we still in the boss area ?
							if (!inBossArea())
							{
								// no, we are not in a boss area
								leaderRadio.updateGameState("Running");
								Log("We are back on the run");
								BossEncounter = false;
								// wait for a bit - probably for the followers to catch up
								pauseForABit(1,2);
							}
						}
						
					
						// Are we on the run
						// leader is on the run and passing coordinates to the followers if GameState is either
						// set to "Running" or "BossEncounter"
						// some Boss Encounters require a bit of a walk
						if (currentGameState == "Running" || currentGameState == "BossEncounter")
						{				
							// ALL OKAY
							// database is in place
							// party is intact
							// we can start
					
							// write coordinates to the PathCoordinates file after a certain amount of time (3 seconds)
							// ========================================================================================
							// we will actuall pass the world ID, Current Level Area ID, and the cooridinates of the leader to the followers
							if (DateTime.Now.Subtract(LastPostionCheck).TotalMilliseconds > 1000)
							{
								LastPostionCheck = DateTime.Now;
								// update coordinates
								leaderRadio.updatePathCoordinates(ZetaDia.CurrentWorldId.ToString(), ZetaDia.CurrentLevelAreaId.ToString(), ZetaDia.Me.Position);
							}
					
							// Check if anyone is missing from the party
							// =========================================
							// this is performed every checkOnPartyTimeGap minutes (plus some 10ths of seconds)
							if (enablePartyCheck && ((DateTime.Now - lastPartyCheckTime).TotalMinutes >= checkOnPartyTimeGap) && !inCombat())
							{
						
								// if any == NotPresent, THEN reinvite
								if (leaderRadio.allPresentInParty(totalNumberOfPartyMembers) == false)
								{
									// EEK, someone has skipped out of 'ere!
									Log("EEK, some biatch has chipped, get 'im back!");
									// re-invite to party
									if (!inCombat())
									{
										// only perform re-invite if we are not in combat
										inviteToTheParty();	
										pauseForABit(1, 3);					
										// set to current time, so that we can check up on the party again after another n number of minutes (checkOnPartyTimeGap)
										lastPartyCheckTime = DateTime.Now;
									}							
								}
							}
					
						}
						// Leader is DEADED!
						else if (currentGameState == "Dead")
						{
							// Create a button object to represent the buttons on the screen. This is used for one button at a time.
							Zeta.Internals.UIElement InGameButton = null;	
			
							// wait for a second or 2
							pauseForABit(1, 2);
			
							// If the Revive at last checkpoint BUTTON pops up, then click on it
							if (Zeta.Internals.UIElement.IsValidElement(0xBFAAF48BA9316742) && (InGameButton = Zeta.Internals.UIElement.FromHash(0xBFAAF48BA9316742)) != null)
							{
								if (InGameButton.IsVisible)
								{
									Log("Revive at last checkpoint BUTTON has popped up");
									// wait for a second or 2
									pauseForABit(1, 2);
									Log("Clicking on the Revive at last checkpoint BUTTON");
									// click on the Revive at last checkpoint BUTTON
									InGameButton.Click();
									// wait for a second or 2
									pauseForABit(1, 2);
								}
							}	

							// TP back to town as long as we are NOT in combat or looting
							// this will force the followers back to the town
							if (!inCombat() && !isLooting() && !ZetaDia.Me.IsInTown)
							{
								ZetaDia.Me.UseTownPortal();
								pauseForABit(2, 4);
							}
			
							// Once we are in town, we should:
							// 1) empty our bag
							// 2) start the run again
							if (ZetaDia.Me.IsInTown)
							{
								// FORCE STASHING ????????????????????
				
								// Reload profile
								Zeta.CommonBot.ProfileManager.Load(GlobalSettings.Instance.LastProfile);				
								pauseForABit(1, 2);
					
								// update GameState to running				
								leaderRadio.updateGameState("Running");				
								pauseForABit(1, 2);			
							}
						}						
						// Party creation stage
						else if (currentGameState == "CreateParty")
						{
							Log("Forming the party up from OnPulse()");
							// form the party
							createParty();
						}
					}
				}
				catch
				{
					// do nothing as if we reach this, then the database was missing and a new game will be created
				}
		
				// Create a button object to represent the buttons on the screen. This is used for one button at a time.
				Zeta.Internals.UIElement FollowerButton = null;
			
				// Follower leaving message
				// need to OKAY this in order to get rid of it
				if (Zeta.Internals.UIElement.IsValidElement(0xF85A00117F5902E9) && (FollowerButton = Zeta.Internals.UIElement.FromHash(0xF85A00117F5902E9)) != null)
				{
					if (FollowerButton.IsVisible)
					{
						Log("Follower is leaving message has popped up");
						// pause for a few seconds
						pauseForABit(1, 3);			
						// click on the OK button
						FollowerButton.Click();
					}
				}
				// Follower is joining up again - if you are the only one in the party, then might as well have the follower rejoin
				// need to OKAY this in order to get rid of it
				if (Zeta.Internals.UIElement.IsValidElement(0x161745BBCE22A8BA) && (FollowerButton = Zeta.Internals.UIElement.FromHash(0x161745BBCE22A8BA)) != null)
				{
					if (FollowerButton.IsVisible)
					{
						Log("Follower is joining message has popped up");
						// pause for a few seconds
						pauseForABit(1, 3);
						// click on the YES button
						FollowerButton.Click();
					}
				}
				
				// Create a button object to represent the buttons on the screen. This is used for one button at a time.
				Zeta.Internals.UIElement BossEncounterButton = null;
			
				// Boss Encounter
				// This clicks on the ACCEPT button of the boss encounter
				// found to be the same hash for all bosses 
				// Skeleton King - The Butcher - Zultan Kull - Belial - Ghom - Azmodan - Diablo
				if (Zeta.Internals.UIElement.IsValidElement(0x69B3F61C0F8490B0) && (BossEncounterButton = Zeta.Internals.UIElement.FromHash(0x69B3F61C0F8490B0)) != null)
				{
					if (BossEncounterButton.IsVisible)
					{
						// click on the ACCEPT button
						BossEncounterButton.Click();
						// set the boss encounter to true, so that we don't test for out of Range of the Leader
						BossEncounter = true;
						// set GameState to BossEncounter, so that the followers knwo what is happening
						// this will help prevent the followers from trying to TP out of the Boss area
						leaderRadio.updateGameState("BossEncounter");
						// Allow time to enter boss area
						while (!inBossArea())
						{
							Log("We are transitioning to boss area");
							pauseForABit(3, 4);
						}
						Log("Time to kick some Boss butt!");
					}
				}
				if (Zeta.Internals.UIElement.IsValidElement(0xF495983BA9BE450F) && (BossEncounterButton = Zeta.Internals.UIElement.FromHash(0xF495983BA9BE450F)) != null)
				{
					if (BossEncounterButton.IsVisible)
					{
						// click on the ACCEPT button
						BossEncounterButton.Click();
						// set the boss encounter to true, so that we don't test for out of Range of the Leader
						BossEncounter = true;
						// set GameState to BossEncounter, so that the followers knwo what is happening
						// this will help prevent the followers from trying to TP out of the Boss area
						leaderRadio.updateGameState("BossEncounter");
						// Allow time to enter boss area
						while (!inBossArea())
						{
							Log("We are transitioning to boss area");
							pauseForABit(3, 4);
						}
						Log("Time to kick some Boss butt!");
					}
				}
			
			}
			
        } // END OF OnPulse()

        /*
            This is executed whenever a new game is created
			Its main purpose here is to create a party
        */
        public void GameChanged(object sender, EventArgs e)
        {			
			// create the folder that will contain the database
			leaderRadio.createCommsFolder(commsFolderPath + commsFolderName);
			// create the database
			leaderRadio.createCommsDatabase(commsFolderPath, totalNumberOfPartyMembers);
				
			// CREATE THE PARTY
			// ****************
			// Upadate GameState to "CreateParty"
			leaderRadio.updateGameState("CreateParty");
        } // END OF GameChanged(object sender, EventArgs e)
		
        /*
            This is executed whenever the current game is left/quit
			This will allow the followers to act on the current state of the game, such
			as to TP and leave game when the leader leaves game
        */
        public void GameLeft(object sender, EventArgs e)
        {
			Log("We are leaving the current game. Time to reset the GameState to CreateParty!");
			// set game state back to form a new party
			leaderRadio.updateGameState("CreateParty");
		} // END OF GameLeft(object sender, EventArgs e)
		
		/*
			This method represent the BotMain event OnStart (Zeta.CommonBot.BotMain.OnStart)
			We need for the party formation to start again
		 */
        public void BotStarted(Zeta.CommonBot.IBot bot)
		{			
			// Upadate GameState to "Running"
			leaderRadio.updateGameState("Running");
			
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
			// Upadate GameState to "Waiting"
			// in order to stop the run
			leaderRadio.updateGameState("Waiting");
		}
		
		/*
			This method creates the party
			It is called when a new game is started, and when the bot is started 
			 (just-in-case the bot is stopped and then restarted started)
		 */
		private void createParty()
		{	
			Initialise_All();
		
			LoadConfigurationFile();
			
			// create the folder that will contain the database
			leaderRadio.createCommsFolder(commsFolderPath + commsFolderName);
			// create the database
			leaderRadio.createCommsDatabase(commsFolderPath, totalNumberOfPartyMembers);
		
			// Create a button object to represent the buttons on the screen. This is used for one button at a time.
			Zeta.Internals.UIElement InGameButton = null;
			
			// 1st round of invites
			Log("Starting a new game");
			Log("Creating party");
			// pause to wait for other char to catch up
			pauseForABit(3, 5);
			
			// open the social menu 
			if (Zeta.Internals.UIElement.IsValidElement(0xAB9216FD8AADCFF9) && (InGameButton = Zeta.Internals.UIElement.FromHash(0xAB9216FD8AADCFF9)) != null)
			{
				InGameButton.Click();
			}
			
			Log("INVITES INCOMING");
			
			// Now to repeat the invites until everyone has been invited			
			while (leaderRadio.allPresentInParty(totalNumberOfPartyMembers) == false)
			{
				
				// grab the current game state
				// if the database no longer exists, we no longer want to send out invites
				currentGameState = leaderRadio.getGameState();
				if (!Directory.Exists(commsDirectory))
					break;
				// guard against Demonbuddy being stopped
				// we don't want this running infitely!
				// pause for a bit
				Log("Party not full yet. Sending out invites!");
				pauseForABit(1, 3);
			
				// Second round of invites
				inviteToTheParty();			
			}

			// Close social menu
			if (Zeta.Internals.UIElement.IsValidElement(0x7B1FD584DA74FA94) && (InGameButton = Zeta.Internals.UIElement.FromHash(0x7B1FD584DA74FA94)) != null)
			{
				InGameButton.Click();
			}
			
			Log("All invites have been accepted, now it's time to party!");
			
			// pause for a bit
			pauseForABit(1, 2);

			Log("Party up, let's go!");
			
			// Update GameState to "Running", so that everybody starts the run
			leaderRadio.updateGameState("Running");
			
		} // END OF createParty()
		
		/*
			This method deals with the invites to the party
		 */
        private void inviteToTheParty()
        {
            // Create a button object to represent the buttons on the screen. This is used for one button at a time.
            Zeta.Internals.UIElement InGameButton = null;
			
            // invite 1st char			
            if (totalNumberOfPartyMembers >= 2 && (Zeta.Internals.UIElement.IsValidElement(0xC590DACA798C3CA4) && (InGameButton = Zeta.Internals.UIElement.FromHash(0xC590DACA798C3CA4)) != null))
            {
                if (InGameButton.IsVisible && InGameButton.IsEnabled)
                {
                    Log("Sending party invite for first char");
                    InGameButton.Click();
                }
            }
			
			// pause for a bit
			pauseForABit(1, 2);
            // invite 2nd char
            if (totalNumberOfPartyMembers >= 3 && (Zeta.Internals.UIElement.IsValidElement(0x270DE7E871AC762B) && (InGameButton = Zeta.Internals.UIElement.FromHash(0x270DE7E871AC762B)) != null))
            {
                if (InGameButton.IsVisible && InGameButton.IsEnabled)
                {
                    Log("Sending party invite for second char");
                    InGameButton.Click();
                }
            }
			
			// pause for a bit
			pauseForABit(1, 2);
            // invite 3rd char
            if (totalNumberOfPartyMembers == 4 && (Zeta.Internals.UIElement.IsValidElement(0x5E598FAE1E003BBE) && (InGameButton = Zeta.Internals.UIElement.FromHash(0x5E598FAE1E003BBE)) != null))
            {
                if (InGameButton.IsVisible && InGameButton.IsEnabled)
                {
                    Log("Sending party invite for third char");
                    InGameButton.Click();
                }
            }
			
			// pause for a bit
			pauseForABit(1, 2);
			
		} // END OF inviteToTheParty()
		
        /*
            This method checks to see if we are in combat
			If we are in combat then it return true, else it return false
         */
        private bool inCombat()
        {
            Log("In combat check!");
			if (Zeta.CommonBot.CombatTargeting.Instance.FirstNpc == null && Zeta.CommonBot.CombatTargeting.Instance.FirstObject == null)
			{
				return false;
			}
			else
			{
                Log("We are unable to do that because as we are in combat!");
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
				case 58493: //  Adria - The Wrecthed Pit			
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
            This is executed whenever the leader dies
        */
        public void OnPlayerDied(object sender, EventArgs e)
        {
            Log("OOPS ME DEAD!");
			
			// TOTAL REWORKING OF THIS SHIT
			
			// update PartyState to show that the Leader just died
			// if this leader s controlled by the bot then the code 
			// within OnPulse() should fire now, and deal with what is 
			// required when the leader dies
			// Humna control of the leader does not need to have the 
			// leader head back to town
			if (leaderControlledByBot)
				leaderRadio.updateGameState("Dead");
			
		} // END OF OnPlayerDied(object sender, EventArgs e)	
		
        /*
            This method initialises a number of variables to the starting values required when the char starts on a new run after a break or
            when DB is first run
         */
        private void Initialise_All()
        {
			// use for spacing out the writes to the PathCoordinates file of the leader's coordinates
			LastPostionCheck = DateTime.Now;
            // initialise to current time, so that we can check on the party status every so often (e.g. every 2 mins)
            lastPartyCheckTime = DateTime.Now;
            partyMemberHasLeft = false;
            // 1st game created
            numberOfGameCreationsInTheLastTenMins = 1;
            // set the game creation monitor to the current time
            // we will need to check 10 minutes from now, how many games were created
            gameCreationMonitorStartTime = DateTime.Now;
		
			// Boss encounter
			BossEncounter = false;
			
            Log("Initialsiation completed!");
        } // END OF Initialise_All()
		
		/*
			This method pauses the bot for a randomly generated amount of time (seconds and tenths of seconds)
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
            if (!File.Exists(@"Plugins\PartyLeaderPro\ConfigSettings"))
            {
                Log("Configuration file does not exist, we are creating a new one based on the default values: ");
				// create a new config file
                SaveConfigurationFile();
                return;
            }
            // Load the config file
            using (StreamReader configReader = new StreamReader(@"Plugins\PartyLeaderPro\ConfigSettings"))
            {
				// read in the first line
				string[] config = configReader.ReadLine().Split('=');
				check2DudePartySetting = Convert.ToBoolean(config[1]);
				// set the total number of required party members
				if (check2DudePartySetting)
					totalNumberOfPartyMembers = 2;
				// read in the second line
				config = configReader.ReadLine().Split('=');
				check3DudePartySetting = Convert.ToBoolean(config[1]);
				// set the total number of required party members
				if (check3DudePartySetting)
					totalNumberOfPartyMembers = 3;
				// read in the third line
				config = configReader.ReadLine().Split('=');
				check4DudePartySetting = Convert.ToBoolean(config[1]);
				// set the total number of required party members
				if (check4DudePartySetting)
					totalNumberOfPartyMembers = 4;
				// read in the fourth line
				config = configReader.ReadLine().Split('=');
				enablePartyCheck = Convert.ToBoolean(config[1]);
				// read in the fifth line
				config = configReader.ReadLine().Split('=');
				checkOnPartyTimeGap = Convert.ToInt32(config[1]);
				// read in the sixth line - leader controlled by bot or human
				config = configReader.ReadLine().Split('=');
				leaderControlledByBot = Convert.ToBoolean(config[1]);
				
				// COMMS system
				config = configReader.ReadLine().Split('=');
				commsFolderPath = @config[1];
				config = configReader.ReadLine().Split('=');
				commsFolderName = config[1];				
				commsDirectory = commsFolderPath + commsFolderName;		
				// pass this info over to the LeaderComms
				leaderRadio.setPathToDatabaseFiles(commsDirectory);
				
                configReader.Close();
            }
        } // END OF LoadConfigurationFile()
		
		// ***********************************************
        // ***** Save the Config GUI's Configuration *****
        // ***********************************************
        private void SaveConfigurationFile()
        {
            FileStream configStream = File.Open(@"Plugins\PartyLeaderPro\ConfigSettings", FileMode.Create, FileAccess.Write, FileShare.Read);
            using (StreamWriter configWriter = new StreamWriter(configStream))
            {
                configWriter.WriteLine("check2DudeParty=" + check2DudeParty.IsChecked.ToString());
                configWriter.WriteLine("check3DudeParty=" + check3DudeParty.IsChecked.ToString());
                configWriter.WriteLine("check4DudeParty=" + check4DudeParty.IsChecked.ToString());
                configWriter.WriteLine("enablePartyCheck=" + enablePartyCheck.ToString());
				// duration between party checks
                configWriter.WriteLine("checkOnPartyTimeGap=" + checkOnPartyTimeGap.ToString());
				// Leader control system (Bot or Human)
                configWriter.WriteLine("leaderControlledByBot=" + leaderControlledByBot.ToString());
				// COMMS system
                configWriter.WriteLine("commsFolderPath=" + commsFolderPath);
                configWriter.WriteLine("commsFolderName=" + commsFolderName);
				// store the comms file and path
				commsDirectory = commsFolderPath + commsFolderName;	
				// pass this info over to the LeaderComms
				leaderRadio.setPathToDatabaseFiles(commsDirectory);
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
                if (!File.Exists(partyLeaderProXamlFile))
                    Log("ERROR: Can't find PartyLeaderPro.xaml");
                try
                {
                    if (configWindow == null)
                    {
                        configWindow = new Window();
                    }
                    StreamReader xamlStream = new StreamReader(@"Plugins\PartyLeaderPro\PartyLeaderPro.xaml");
                    DependencyObject xamlContent = XamlReader.Load(xamlStream.BaseStream) as DependencyObject;
                    configWindow.Content = xamlContent;
					
					// 3 RADIO BUTTONS - check2DudeParty, check3DudeParty, check4DudeParty
                    check2DudeParty = LogicalTreeHelper.FindLogicalNode(xamlContent, "check2DudeParty") as RadioButton;
                    check2DudeParty.Checked += new RoutedEventHandler(check2DudeParty_Checked);
					
                    check3DudeParty = LogicalTreeHelper.FindLogicalNode(xamlContent, "check3DudeParty") as RadioButton;
                    check3DudeParty.Checked += new RoutedEventHandler(check3DudeParty_Checked);
					
                    check4DudeParty = LogicalTreeHelper.FindLogicalNode(xamlContent, "check4DudeParty") as RadioButton;
                    check4DudeParty.Checked += new RoutedEventHandler(check4DudeParty_Checked);
					
					// BUTTON - btnCheckPartyIntegrity
                    btnCheckPartyIntegrity = LogicalTreeHelper.FindLogicalNode(xamlContent, "btnCheckPartyIntegrity") as Button;
                    btnCheckPartyIntegrity.Click += new RoutedEventHandler(btnCheckPartyIntegrity_Click);

					// text/info below the Check Party Inegrity combo box
                    lblComboBoxInfo1 = LogicalTreeHelper.FindLogicalNode(xamlContent, "lblComboBoxInfo1") as Label;
                    lblComboBoxInfo2 = LogicalTreeHelper.FindLogicalNode(xamlContent, "lblComboBoxInfo2") as Label;
                    lblComboBoxInfo3 = LogicalTreeHelper.FindLogicalNode(xamlContent, "lblComboBoxInfo3") as Label;
					
					// BUTTON - Leader control				
                    btnLeaderControl = LogicalTreeHelper.FindLogicalNode(xamlContent, "btnLeaderControl") as Button;
                    btnLeaderControl.Click += new RoutedEventHandler(btnLeaderControl_Click);
					
					// displays an error message if the database path does not exist
                    lblMessage = LogicalTreeHelper.FindLogicalNode(xamlContent, "lblMessage") as Label;
					
					// set the time between party integrity checks 
                    cmbCheckOnPartyTimeGap = LogicalTreeHelper.FindLogicalNode(xamlContent, "cmbCheckOnPartyTimeGap") as ComboBox;
                    cmbCheckOnPartyTimeGap.SelectionChanged += cmbCheckOnPartyTimeGap_SelectionChanged;
					
					// COMMS SYSTEM SET UP
                    txtCommsSystemFolderPath = LogicalTreeHelper.FindLogicalNode(xamlContent, "txtCommsSystemFolderPath") as TextBox;
                    txtCommsSystemFolderPath.TextChanged += txtCommsSystemFolderPath_TextChanged;					
					
                    txtCommsSystemFolderName = LogicalTreeHelper.FindLogicalNode(xamlContent, "txtCommsSystemFolderName") as TextBox;
                    txtCommsSystemFolderName.TextChanged += txtCommsSystemFolderName_TextChanged;;
					
					// Exit and save button
                    btnDone = LogicalTreeHelper.FindLogicalNode(xamlContent, "btnDone") as Button;
                    btnDone.Click += new RoutedEventHandler(btnDone_Click);
					
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
        }
		
		/*
			This method initialises the controls to their default settings when the config window is displayed
		 */
        private void configWindow_Loaded(object sender, RoutedEventArgs e)
        {
			// Load the configuration file for the config window
			LoadConfigurationFile();
			// default setting for number of people in the party
			check2DudeParty.IsChecked = check2DudePartySetting;
			check3DudeParty.IsChecked = check3DudePartySetting;
			check4DudeParty.IsChecked = check4DudePartySetting;	
			// default settings for the check on the party state (checking if anyone has anyone left the party)
			// set the button's text to YES
			cmbCheckOnPartyTimeGap.SelectedIndex = checkOnPartyTimeGap - 1;
			if (enablePartyCheck == true)
			{
				btnCheckPartyIntegrity.Content = "YES";
				displayCheckOnPartyTimeGapComboBox(true);
			}
			else
			{
				btnCheckPartyIntegrity.Content = "NO";
				displayCheckOnPartyTimeGapComboBox(false);
			}
			// Leader controlled by Bit or Human
			if (leaderControlledByBot == true)
			{
				btnLeaderControl.Content = "Bot Controlled";
			}
			else
			{
				btnLeaderControl.Content = "Human Controlled";
			}
			// COMMS system database
			txtCommsSystemFolderPath.Text = commsFolderPath;
			txtCommsSystemFolderName.Text = commsFolderName;
        }
		
		/*
			This method closes the config window
		 */
        private void configWindow_Closed(object sender, EventArgs e)
        {
            configWindow = null;
        }

		/*
			These 3 methods are the events of the radio buttons, and relate to the number of
			people in the party
		 */
        private void check2DudeParty_Checked(object sender, RoutedEventArgs e)
        {
            totalNumberOfPartyMembers = 2;
        }
        private void check3DudeParty_Checked(object sender, RoutedEventArgs e)
        {
            totalNumberOfPartyMembers = 3;
        }
        private void check4DudeParty_Checked(object sender, RoutedEventArgs e)
        {
            totalNumberOfPartyMembers = 4;
        }
		
		/*
			This method represents the btnCheckPartyIntegrity button event when someone left mouse clicks on the button
			btnCheckPartyIntegrity activates and deactivates the checking by the leader of the integrity of the party
			true/YES = leader will check every so often to see if anybody has left the party
			false/NO = leader will not check on the integrity of the party
		 */
        private void btnCheckPartyIntegrity_Click(object sender, RoutedEventArgs e)
        {
			if (enablePartyCheck == true)
			{
				// set is to false
				enablePartyCheck = false;				
				// set the button's text to NO
				btnCheckPartyIntegrity.Content = "NO";
				// hide the combobox and related text for the Check on Party Time Gap				
				displayCheckOnPartyTimeGapComboBox(false);
			}
			else // enablePartyCheck is currently set to false
			{
				// set is to true
				enablePartyCheck = true;				
				// set the button's text to YES
				btnCheckPartyIntegrity.Content = "YES";
				// display the combobox and related text for the Check on Party Time Gap
				displayCheckOnPartyTimeGapComboBox(true);
			}
        } // END OF btnCheckPartyIntegrity_Click(object sender, RoutedEventArgs e)
		
		/*
			This method initialises the controls to their default settings when the config window is displayed
		 */
        private void displayCheckOnPartyTimeGapComboBox(bool displayAll)
        {
			if (displayAll == true)
			{
				// make the cmbCheckOnPartyTimeGap comboBox visible
				cmbCheckOnPartyTimeGap.Visibility = System.Windows.Visibility.Visible;
				// make the info text below the comboBox visible
				lblComboBoxInfo1.Visibility = System.Windows.Visibility.Visible;
				// make the info text below the comboBox visible
				lblComboBoxInfo2.Visibility = System.Windows.Visibility.Visible;
				// make the info text below the comboBox visible
				lblComboBoxInfo3.Visibility = System.Windows.Visibility.Visible;
			}
			else
			{
				// make the cmbCheckOnPartyTimeGap comboBox invisible
				cmbCheckOnPartyTimeGap.Visibility = System.Windows.Visibility.Hidden;
				// make the info text below the comboBox invisible
				lblComboBoxInfo1.Visibility = System.Windows.Visibility.Hidden;
				// make the info text below the comboBox invisible
				lblComboBoxInfo2.Visibility = System.Windows.Visibility.Hidden;
				// make the info text below the comboBox invisible
				lblComboBoxInfo3.Visibility = System.Windows.Visibility.Hidden;
			}
		} // END OF setupCheckOnPartyTimeGapComboBox(bool displayAll)
		
		/*
			This method represents the cmbCheckOnPartyTimeGap event, which sets the time between checks on the party's integrity
		 */		
        private void cmbCheckOnPartyTimeGap_SelectionChanged(object sender, RoutedEventArgs e)
        {
			switch (cmbCheckOnPartyTimeGap.SelectedIndex)
			{
				case 0:
					checkOnPartyTimeGap = 1;
					break;
				case 1:
					checkOnPartyTimeGap = 2;
					break;
				case 2:
					checkOnPartyTimeGap = 3;
					break;
				case 3:
					checkOnPartyTimeGap = 4;
					break;
				case 4:
					checkOnPartyTimeGap = 5; 
					break;
				case 5:
					checkOnPartyTimeGap = 6;
					break;
				case 6:
					checkOnPartyTimeGap = 7; 
					break;
				case 7:
					checkOnPartyTimeGap = 8;
					break;
				case 8:
					checkOnPartyTimeGap = 9;
					break;
				case 9:
					checkOnPartyTimeGap = 10;
					break;
				case 10:
					checkOnPartyTimeGap = 11;
					break;
				case 11:
					checkOnPartyTimeGap = 12;
					break;
				case 12:
					checkOnPartyTimeGap = 13; 
					break;
				case 13:
					checkOnPartyTimeGap = 14;
					break;
				default:
					checkOnPartyTimeGap = 15;
					break;
			}
				
        } // END OF cmbCheckOnPartyTimeGap_SelectionChanged(object sender, RoutedEventArgs e)
		
		
		
		/*
			This method represents the txtCommsSystemFolderPath TextBox TextChanged event 
			this text box holds the path to the comms database folder 
			(it does not include the folder name)
		 */		
        private void txtCommsSystemFolderPath_TextChanged(object sender, RoutedEventArgs e)
        {
			// store contents 
			commsFolderPath = @txtCommsSystemFolderPath.Text;
		}
		
		/*
			This method represents the txtCommsSystemFolderName TextBox TextChanged event 
			this text box holds the name of the comms database's folder 
		 */		
        private void txtCommsSystemFolderName_TextChanged(object sender, RoutedEventArgs e)
        {
			// store contents 
			commsFolderName = txtCommsSystemFolderName.Text;
		}
		
		/*
			This method represents the btnLeaderControl button click event
			It sets how the leader of the party will be controlled. Either by the bot or by a human
		 */		
        private void btnLeaderControl_Click(object sender, RoutedEventArgs e)
        {
			if (leaderControlledByBot)
			{
				btnLeaderControl.Content = "Human Controlled";
				leaderControlledByBot = false;
			}
			else
			{
				btnLeaderControl.Content = "Bot Controlled";
				leaderControlledByBot = true;	
			}
		}
		
		/*
			This method represents the btnDone button event when someone left mouse clicks on the button
			It closes the configuration window
		 */		
        private void btnDone_Click(object sender, RoutedEventArgs e)
        {
			// check that the path exists   @txtCommsSystemFolderPath.Text)
            if(Directory.Exists(@txtCommsSystemFolderPath.Text))
            {
				// make sure the error message is hidden
				lblMessage.Visibility = System.Windows.Visibility.Hidden;	
				// Save the current config window settings to the config file
				SaveConfigurationFile();
				// close the config GUI
				configWindow.Close();
			
				LoadConfigurationFile();
			
				// create the folder that will contain the database
				leaderRadio.createCommsFolder(commsFolderPath + commsFolderName);
				// create the database
				leaderRadio.createCommsDatabase(commsFolderPath, totalNumberOfPartyMembers);
			}
			else
			{
				// invalid path
				// inform the user that the path does not exist
				lblMessage.Visibility = System.Windows.Visibility.Visible;	
			}
        }
		
        #endregion
        // ***************************************************
        // *********** END OF CONFIG WINDOW REGION ***********
        // ***************************************************

    }
	
}

