using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Markup;
using System.Configuration;

using Zeta;
using Zeta.Common;
using Zeta.CommonBot;
using Zeta.TreeSharp;
using Zeta.Common.Plugins;
using Zeta.CommonBot.Profile;
using Zeta.CommonBot.Profile.Common;
using Zeta.XmlEngine;
using Action = Zeta.TreeSharp.Action;
using Zeta.CommonBot.Settings;
using Zeta.Internals.Actors;
using Zeta.Internals.SNO;
using Zeta.Internals.Service;


/*
	PartyDudeComms
	
	This deals with the communication between the PartyDudePro plugin and the plugin's comms database
	
	Author: ChuckyEgg (CIGGARC Developer)
	Support: CIGGARC team, et al
	Date: 3rd of November, 2012
	Verion: 2.0.0
	
 */
 
namespace PartyDudePro
{
    public class PartyDudeComms
    {			
		// tables/files that make up the database
		private string commsDirectory = @"..\CommsCentre";
		private string partyState = @"..\CommsCentre\PartyState";
		private string dude1State = @"..\CommsCentre\Dude1State";
		private string dude2State = @"..\CommsCentre\Dude2State";
		private string dude3State = @"..\CommsCentre\Dude3State";
		// holds the exact Dude?State file for the party member using the plugin
		// it will be assigned either dude1State, dude2State, or dude3State
		// it is assigned its value when the PartyDudePro gets the Dude's Party ID - retrievePartyMemberID()
		private string dudeStateFile = "";
		private string gameState = @"..\CommsCentre\GameState";
		private string partyMemberID = @"..\CommsCentre\PartyMemberID";
		private string pathCoordinates = @"..\CommsCentre\PathCoordinates";
		
		// this is used by the party members in the retrieval of their party member ID
		// the PartyMemberID file is renamed to PartyMemberIDTransitionFile, so that no other bot can use the file while
		// the current bot retrieves its ID
        private string partyStateTransitionFile = @"..\CommsCentre\PartyStateTransitionFile";
		// this is used by the party members in the retrieval of their party member ID
		// the PartyMemberID file is renamed to PartyMemberIDTransitionFile, so that no other bot can use the file while
		// the current bot retrieves its ID
        private string partyMemberIDTransitionFile = @"..\CommsCentre\PartyMemberIDTransitionFile";
		
		// used to generate a random value to make the action appear more natural
		private Random randomNumber = new Random();
		private int randomTime = 0;
		private int randomSeconds = 0;
		private int randomTenthsOfSeconds = 0;
		// this is a unique ID, that is used to identify it in the party, for communication between
		// party members
		private string partyID = "";
		// this represents what is happening within the game, and specifically to the leader
		// it can be set to CreateGame, Running, or Stashing
		private string currentGameState = "CreateGame";
		
		// this represents what is happening or happened to the Dude (party memeber)
		// it can be set to Waiting, Running, Stashing, Dead, or Mayday
		// not all of these states have been implemented as yet
		private string currentDudeState = "Waiting";
		
		// holds the last coordinates successfully retrieved from the PathCoordinates file (Leader's last coordinates)
		private string[] lastSuccessfullyReadCoords;
	
		/*
			class constructor
		*/
		public PartyDudeComms()
		{
	
		}

		/*
			This method is used to write to the log window of DemonBuddy
		 */
        public void Log(string message, LogLevel level = LogLevel.Normal)
        {
            Logging.Write(level, string.Format("{0}", message));
        }
		
		/*
			This method gets the party dude's ID from the PartyMemberID database file
			This is a unique ID, and identifies it to the rest of the party
			thus enabling communication between specific party members
			
			This methods needs to read in the 1st ID in the file, which is then the party member's ID
			then it must delete and recreate the file, without the ID it has taken
			thus leaving the other IDs for the remaining party members
		 */
		public string retrievePartyMemberID()
		{
            // Check if file exist and that we can use it (read/write)
            // repeat until we can use the file
			
			Log("Attempting to get my party ID");
			
            bool IDretrieved = false;
            // keep trying till we get the ID
            while (IDretrieved == false)
            {
				// only try to work on file if it exists
				// if it does not exist, it is probably being processed by another party member
				if (File.Exists(partyMemberID))
				{
					try
					{
						// move the file, ready for processing, thus stopping others from using it
						File.Move(partyMemberID, partyMemberIDTransitionFile);
						
					    // store the rest of the file, as we need to create a new file with its contents
						ArrayList list = new ArrayList();
						
						// try and get the ID
						// if the file is already in use by someone else, this should fail, and then try again
						try
						{	
							using (FileStream fs = File.Open(partyMemberIDTransitionFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
							{
								// retrieve and store the member's ID
								using (StreamReader partyMemberIDReader = new StreamReader(fs))
								{
									// grab the member's ID
									string[] line = partyMemberIDReader.ReadLine().Split('=');
									// store the ID
									// this is this member's unique ID
									partyID = line[1];

									if(partyID == "Dude1")
										dudeStateFile = @dude1State;
									else if(partyID == "Dude2")
										dudeStateFile = @dude2State;
									else dudeStateFile = @dude3State;
									Log("***********************************");
									Log("Party ID = " + @dudeStateFile);
									Log("***********************************");
									pauseForABit(8,10);
								
									while (!partyMemberIDReader.EndOfStream)
									{
										list.Add(partyMemberIDReader.ReadLine());
									}
								}	
								// close the file, then delete it
								fs.Close();
								File.Delete(partyMemberIDTransitionFile);					
							}
							
							// Recreate the PartyMemberID file with the updated contents (less one ID)
							try
							{
								using (FileStream filesStrm = File.Open(partyMemberID, FileMode.Create, FileAccess.Write, FileShare.None))
								{
									// Write the remaining IDs to the file
									using (StreamWriter partyMemberIDWriter = new StreamWriter(filesStrm))
									{
										foreach (string line in list)
										{
											// store ID in file
											partyMemberIDWriter.WriteLine(line);
										}
									}
									// close the file, allowing others to take control of it
									filesStrm.Close();
								}
								
								// we have successfully processed the PartyMemberID file
								IDretrieved = true;
							}
							catch
							{
								Log("Unable to open PartyMemberID... trying again!");
							}
							
						}
						catch
						{
							Log("Unable to open PartyMemberIDTransitionFile... trying again!");
						}
						
					}
					catch
					{
						Log("Unable to move the partyMemberID file to the partyMemberIDTransitionFile. Will try again.");
					}
					
                }
                else
				{
					// file does not exist
					// it must be in use by another party member
					// wait for a bit	
				}
            }
			
			return partyID;
		
		} // END OF retrievePartyMemberID()
		
		/*
			This method sets the variable (commsDirectory) holding the comms folder's path and name to that entered by the user
			It also adds this path to the variables that hold the database file names
		 */
		public void setCommsFolderPathAndName(string commsFolderPathAndName)
		{		
			// store the comms files path and name
			commsDirectory = commsFolderPathAndName;
			// set up the path and name to the database files
			partyState = commsDirectory + @"\PartyState";
			gameState = commsDirectory + @"\GameState";
			partyMemberID = commsDirectory + @"\PartyMemberID";
			pathCoordinates = commsDirectory + @"\PathCoordinates";
			
			dude1State = commsDirectory + @"\Dude1State";
			dude2State = commsDirectory + @"\Dude2State";
			dude3State = commsDirectory + @"\Dude3State";
			
			// transition files
			// required as the original files will be deleted and recreated by more than one party member/process and
			// the use of transition files ensures that only one member/process has total use of the file
			partyStateTransitionFile = commsDirectory + @"\PartyStateTransitionFile";
			partyMemberIDTransitionFile = commsDirectory + @"\PartyMemberIDTransitionFile";
		} // END OF setCommsFolderPathAndName(string commsFolderPathAndName)
		
		/*
			This method updates the PartyState file to show that this bot/character is present in the party
		 */
		public void updatePartyState(string strPartyState)
		{
            // Check if file exist and that we can use it (read/write)
            // repeat until we can use the file

            bool stateUpdated = false;
            // keep trying till we get the ID
            while (stateUpdated == false)
            {
				// only try to work on file if it exists
				// if it does not exist, it is probably being processed by another party member
                if (File.Exists(partyState))
                {
					try
					{
						// move the file, ready for processing, thus stopping others from using it
						File.Move(partyState, partyStateTransitionFile);
						
						// store the rest of the file, as we need to create a new file with its contents
						ArrayList list = new ArrayList();
						
						// try and get the ID
						// if the file is already in use by someone else, this should fail, and then try again
						try
						{
							using (FileStream fs = File.Open(partyStateTransitionFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
							{
								// retrieve and store the member's ID
								using (StreamReader partyStateReader = new StreamReader(fs))
								{			
									string[] line;
									string newLine = "";
								
									while (!partyStateReader.EndOfStream)
									{						
										line = partyStateReader.ReadLine().Split('=');
										if (line[0] == partyID)
											line[1] = strPartyState;
								
										newLine = line[0] + "=" + line[1];
										list.Add(newLine);
									}
								}	
								// close the file, then delete it
								fs.Close();
								File.Delete(partyStateTransitionFile);					
							}
							
							// Recreate the PartyState file with the updated contents (one of the settings set to "Present")
							try
							{
								using (FileStream filesStrm = File.Open(partyState, FileMode.Create, FileAccess.Write, FileShare.None))
								{
									// Write the remaining IDs to the file
									using (StreamWriter partyStateWriter = new StreamWriter(filesStrm))
									{
										foreach (string line in list)
										{
											// store ID in file
											partyStateWriter.WriteLine(line);
										}
									}
									// close the file, allowing others to take control of it
									filesStrm.Close();
								}

								// we have successfully processed the PartyMemberID file
								stateUpdated = true;
							}
							catch
							{
								Log("Unable to recreate the PartySate file - this may cause problem with party control!");
							}
								
						}
						catch
						{
							Log("Failed to open the PartyStateTransition file - party formation will fail!");
						}
						
					}
					catch
					{
						Log("Unable to move partyState to partyStateTransitionFile. Trying again!");
					}
						
                }
                else
				{
					// file does not exist
					// it must be in use by another party member
					// wait for a bit
				}
            }
			
		} // END OF updatePartyState()
		
		/*
			this updates a specific Dude's (member's) file and represents the state of the party member (PartyDudePro)
				Waiting (waiting for the PartyLeaderPro to start running)
				Running
				Dead
				Stashing
				Mayday
				
			Param: dudeFile is the path to and name of the specific member's files (c:\Temp\Comms\Dude1State)
			Param: currentDudeState is the current state of the party member (Waiting, Running, Dead, Mayday)
		 */
		public void updateDudeState(string newDudeState)
		{
			bool stateUpdated = false;
			while (!stateUpdated)
			{
				try
				{
					FileStream fs = File.Open(dudeStateFile, FileMode.Create, FileAccess.Write, FileShare.Read);
					using (StreamWriter dudeStateWriter = new StreamWriter(fs))
					{			
						dudeStateWriter.WriteLine(newDudeState);
					}
					fs.Close();
					// state successfully updated, therefore we can exit this method :)
					stateUpdated = true;
				}
				catch
				{
				}
			}
			
		} // END OF updateDudeState(string dudeFile, string currentDudeState)
	
		/*
			this method retrieves the current member's state from their Dude?State file (? will be 1, 2, or 3)
			  Waiting - party members wait till leader is ready to start the run
			  Running - Dude is following the leader during the run
			  Stashing - Dude is stashing
			  Dead - dude got owned!
		 */
		public string getDudeState()
		{		
			bool dudeStateAcquired = false;
			string currentState = "";
			while (!dudeStateAcquired)
			{
				try
				{
					FileStream fs = File.Open(dudeStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
					using (StreamReader dudeStateReader = new StreamReader(fs))
					{			
						currentState = dudeStateReader.ReadLine();
					}
					fs.Close();
					// only store and pass the value read from the file that is a valid state for a Dude
					if (currentState == "Running" || currentState == "Waiting" || currentState == "Dead" ||currentState == "Mayday" )
						currentDudeState = currentState;
					else
					{
						// file contains invalid information, need to correct this
						currentDudeState = "Running";
						updateDudeState("Running");
					}
					// state successfully updated, therefore we can exit this method :)
					dudeStateAcquired = true;
				}
				catch
				{
					// balls, could not access the file
					Log("==============================================");
					Log("EEK! Could not access the " + @dudeStateFile + " file!");
				}
			}
			
			return currentDudeState;
			
		} // END OF getDudeState()
		
		/*
			This method checks to see if the party member using this bot is present in the party
		 */
		public bool thisDudeIsPresent(string dude)
		{
			// access the PartyState file, to see if anybody is still not in the party
			// does file exist?
			if (!File.Exists(partyState))
			{
				// someone may be using it
				return false; 
			}
			
			
			// Open the PartyState file to be read. Allow the party member to write to it
         //   FileStream fs = File.Open(partyState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using (StreamReader partyStateReader = new StreamReader(partyState))
            {		
				// read in the state of Dude1 
				string[] state;

				while (!partyStateReader.EndOfStream)
				{
					state = partyStateReader.ReadLine().Split('=');
                    if (state[0] == dude)
                    {
                        switch (dude)
                        {
                            case "Dude1":
								if (state[1] == "Present")
									return true;
                                break;
                            case "Dude2":
								if (state[1] == "Present")
									return true;
                                break;
                            case "Dude3":
								if (state[1] == "Present")
									return true;
                                break;
						}
						
					}
					
				}
					
			}	
			// close the file, allowing others to take control of it
         //   fs.Close();
			
			// Dude is not set to Present, therefore not shown as being in the party
			return false;
		}
	
		/*
			this method retrieves the current GameState from the GameState file
			  CreateParty - party members wait till leaders finishes creating the party (they stand still)
			  Running - leader is exploring, the party members can follow
			  Stashing - Leader is emptying bags, the members can wait (stand still/fight/loot)
		 */
		public string getGameState()
		{
			// does file exist?
			// If not, then the leader if probably changing its contents
			// e.g. CreateGame - Running - Stashing
			try
			{
				if (File.Exists(gameState))
				{
					try
					{
						// open the GameState file for reading
						FileStream fs = File.Open(gameState, FileMode.Open, FileAccess.Read, FileShare.Read);
						using (StreamReader gameStateReader = new StreamReader(fs))
						{
							currentGameState = gameStateReader.ReadLine();
						}
						fs.Close();
						return currentGameState;
					}
					catch
					{
					}
				}
			}
			catch
			{
				// GameState file does not exist
			}
			// file might be in the process of being updated by the leader
			// stick with the last GameState
			return currentGameState;
			
		} // END OF getGameState()
	
		/*
			this method retrieves the current coordinates the leader posted to the PathCoordinates file
			this is only done if the file is accessible
		 */
		public string[] getPathCoordinates()
		{
			string[] currentLocationDetails;
			// I NEED TO CREATE A DEFAULT VALUE FOR THIS
			// does file exist?
			try
			{
				if (File.Exists(pathCoordinates))
				{
					// grab the coordinates
					try
					{
						// open the GameState file for reading
						FileStream fs = File.Open(pathCoordinates, FileMode.Open, FileAccess.Read, FileShare.Read);
						using (StreamReader pathCoordinatesReader = new StreamReader(fs))
						{
							currentLocationDetails = pathCoordinatesReader.ReadLine().Split('=');
						}
						fs.Close();
						// store this, just-in-case the next value cannot be retrieved
						lastSuccessfullyReadCoords = currentLocationDetails;
						return currentLocationDetails;
					}
					catch
					{
					}
					// return previous value, as file is not accessible
					return lastSuccessfullyReadCoords;
				}
			}
			catch
			{
			}
			// return previous value, as file is not accessible
			return lastSuccessfullyReadCoords;
		
		} // END OF getPathCoordinates()
		
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
	
	
    }
	
}

