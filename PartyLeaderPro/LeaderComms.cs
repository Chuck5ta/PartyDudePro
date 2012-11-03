using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Threading;
using System.Windows.Markup;
using System.Windows.Forms;
using System.Configuration;

using Zeta;
using Zeta.Common;
using Zeta.CommonBot;
using Zeta.Common.Plugins;
using Zeta.Internals;
using Zeta.Internals.Actors;
using Zeta.Internals.Service;
using Zeta.TreeSharp;
using Zeta.CommonBot.Profile;
using Zeta.CommonBot.Profile.Common;
using Zeta.XmlEngine;
using Action = Zeta.TreeSharp.Action;
using Zeta.CommonBot.Settings;
using Zeta.Internals.SNO;


/*
	LeaderComms
	
	This deals with the communication between the PartyLeaderPro plugin and the plugin's comms database	
	
	Author: ChuckyEgg (CIGGARC Developer)
	Support: CIGGARC team, et al
	Date: 3rd of November, 2012
	Verion: 2.0.0
	
 */
 
namespace PartyLeaderPro
{
    public class LeaderComms
    {
		// directory where the database will be held
		private DirectoryInfo di;
		// tables/files that make up the database
		private string commsFolderPath = @"..\";
		private string commsFolderName = "CommsCentre";
		private string commsDirectory = @"..\CommsCentre";
		private string partyState = @"..\CommsCentre\PartyState";
		private string dude1State = @"..\CommsCentre\Dude1State";
		private string dude2State = @"..\CommsCentre\Dude2State";
		private string dude3State = @"..\CommsCentre\Dude3State";
		private string gameState = @"..\CommsCentre\GameState";
		private string partyMemberID = @"..\CommsCentre\PartyMemberID";
		private string pathCoordinates = @"..\CommsCentre\PathCoordinates";
		
		// used to generate a random value to make the action appear more natural
		private Random randomNumber = new Random();
		private int randomTime = 0;
		private int randomSeconds = 0;
		private int randomTenthsOfSeconds = 0;		
		
		// this represents what is happening within the game, and specifically to the leader
		// it can be set to CreateGame, Running, or Stashing
		private string currentGameState = "CreateGame";
		
		private FileStream pathCoordinatesFS;
	
		/*
			class constructor
		*/
		public LeaderComms()
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
			this creates the folder that the comms database will reside in
			this folder and its contents will be deleted once botting session is over
		 */
		public void createCommsFolder(string commsPathAndFolderName)
		{
			commsDirectory = commsPathAndFolderName;
            // Determine whether the directory exists. 
            if (Directory.Exists(commsDirectory)) 
            {
				// delete the comms folder
				Directory.Delete(commsDirectory, true);
			}
			// Create the comms folder.
			di = Directory.CreateDirectory(commsDirectory);
			
		} // END OF createCommsFolder()
	
		/*
			this deletes the folder that the comms database will reside in
		 */
		public void deleteCommsFolder()
		{
            // Delete the directory and its contents
            di.Delete(true);			
		} // END OF deleteCommsFolder()
	
		/*
			this creates the comms database, that will be used by the comms to transmit and receive 
			mesages between themselves
		 */
		public void createCommsDatabase(string pathToDatabase, int totalNumberOfPartyMembers)
		{
			// store the current path
			commsFolderPath = pathToDatabase;
			// set up the path and name to the database files
			setPathToDatabaseFiles(commsDirectory);
			
			// create the PartyState file
			// this file retresents each of the party members (dudes), and shows if they are in the party (Present), or not in the party (NotPresent)
			// initial setting will be all Not Present
			// each bot will be assigned a location in the party (Dude1, Dude2, Dude3), and they will update the PartyState accordingly
			createPartyState(totalNumberOfPartyMembers);
			
			// create the files and set default values for Leader and Follower state
			createDudeState(totalNumberOfPartyMembers);
			
			
			// create the GameState file
			// GameState represents what the leader is currently doing, and its contents dictate what the party members do
			updateGameState("CreateParty");
			// create the PartyMemberID file
			// this file is used to identify the party members, and contains 
			// the IDs, dude1, dude2, dude3, and dude4
			// as each member joins the party, they take one of these IDs
			createPartyMemberID(totalNumberOfPartyMembers);
			// this creates the pathCoordinates file
			// this holds the coordinates of the path as the leader walks it
			// the last coordinate in the file is the leader's current coordinate
			createPathCoordinates();
		
		} // END OF createCommsDatabase(int totalNumberOfPartyMembers)
		
		
	
		/*
			this method sets the current path to the database files
		 */
		public void setPathToDatabaseFiles(string pathToCommsDirectory)
		{
			commsDirectory = pathToCommsDirectory;
			partyState = commsDirectory + @"\PartyState";
			gameState = commsDirectory + @"\GameState";
			partyMemberID = commsDirectory + @"\PartyMemberID";
			pathCoordinates = commsDirectory + @"\PathCoordinates";	
				
			dude1State = commsDirectory + @"\Dude1State";
			dude2State = commsDirectory + @"\Dude2State";
			dude3State = commsDirectory + @"\Dude3State";		
		}
		
	
		/*
			this method updates the GameState
			GameState represents what the leader is currently doing, and its contents dictate what the party members do
			GameStates:
			  CreateParty - party members wait till leaders finishes creating the party (they stand still)
			  Running - leader is exploring, the party members can follow
			  Stashing - Leader is emptying bags, the members can wait (stand still/fight/loot)
		 */
		public void updateGameState(string currentGameState)
		{
			// does file exist?
			if (File.Exists(gameState))
			{
				// delete file
				File.Delete(gameState);
			}
			
			// create file with desired contents
            FileStream fs = File.Open(gameState, FileMode.Create, FileAccess.Write, FileShare.Read);
            using (StreamWriter gameStateWriter = new StreamWriter(fs))
            {
                gameStateWriter.Write(currentGameState);
            }
            fs.Close();
			
		} // END OF updateGameState(string currentGameState)
	
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
			this method create the PartyState file
			this represents the members of the party (not the leader), and shows if they are in the party (Present) or not (NotPresent)
		 */
		public void createPartyState(int totalNumberOfPartyMembers)
		{			
			try
			{
				// create file with desired contents
				FileStream fs = File.Open(partyState, FileMode.Create, FileAccess.Write, FileShare.Read);
				using (StreamWriter gameStateWriter = new StreamWriter(fs))
				{
					// build the file based on the number of people to bein the party
					if (totalNumberOfPartyMembers >= 2)
					{
						gameStateWriter.WriteLine("Dude1=NotPresent");
					}
					// build the file based on the number of people to bein the party
					if (totalNumberOfPartyMembers >= 3)
					{
						gameStateWriter.WriteLine("Dude2=NotPresent");
					}
					// build the file based on the number of people to bein the party
					if (totalNumberOfPartyMembers == 4)
					{
						gameStateWriter.WriteLine("Dude3=NotPresent");
					}
				}
				// close the file, allowing others to take control of it
				fs.Close();
			}
			catch
			{
			}
			
		} // END OF createPartyState(int totalNumberOfPartyMembers)	
		
		/*
			this creates the files that represents for state of the party members (PartyDudePro)
				Waiting (waiting for the PartyLeaderPro to start running)
				Running
				Dead
				Stashing
				Mayday
		 */
		public void createDudeState(int totalNumberOfPartyMembers)
		{
			// build the file based on the number of people to be in the party
			// *** Dude1 ***
			if (totalNumberOfPartyMembers >= 2)
			{
				// create file with desired contents
				// Dude1
				FileStream fs = File.Open(dude1State, FileMode.Create, FileAccess.Write, FileShare.Read);
				using (StreamWriter dudeStateWriter = new StreamWriter(fs))
				{			
					dudeStateWriter.WriteLine("Waiting");
				}
				fs.Close();
			}
			// *** Dude2 ***
			if (totalNumberOfPartyMembers >= 3)
			{
				// create file with desired contents
				// Dude2
				FileStream fs = File.Open(dude2State, FileMode.Create, FileAccess.Write, FileShare.Read);
				using (StreamWriter dudeStateWriter = new StreamWriter(fs))
				{			
					dudeStateWriter.WriteLine("Waiting");
				}
				fs.Close();
			}
			// *** Dude3 ***
			if (totalNumberOfPartyMembers == 4)
			{
				// create file with desired contents
				// Dude3
				FileStream fs = File.Open(dude3State, FileMode.Create, FileAccess.Write, FileShare.Read);
				using (StreamWriter dudeStateWriter = new StreamWriter(fs))
				{			
					dudeStateWriter.WriteLine("Waiting");
				}
				fs.Close();
			}			
		} // END OF createDudeState(int totalNumberOfPartyMembers)
		
		/*
			create the PartyMemberID database file/record
			this file is used to identify the party members, and contains 
			the IDs, dude1, dude2, dude3, and dude4
			as each member joins the party, they take one of these IDs
		 */
		public void createPartyMemberID(int totalNumberOfPartyMembers)
		{				
			try
			{		
				// create file with desired contents
				FileStream fs = File.Open(partyMemberID, FileMode.Create, FileAccess.Write, FileShare.Read);
				using (StreamWriter gameStateWriter = new StreamWriter(fs))
				{
					// build the file based on the number of people to be in the party
					if (totalNumberOfPartyMembers >= 2)
					{
						gameStateWriter.WriteLine("Dude1=Dude1");
					}
					// build the file based on the number of people to be in the party
					if (totalNumberOfPartyMembers >= 3)
					{
						gameStateWriter.WriteLine("Dude2=Dude2");
					}
					// build the file based on the number of people to be in the party
					if (totalNumberOfPartyMembers == 4)
					{
						gameStateWriter.WriteLine("Dude3=Dude3");
					}
				}
				// close the file, allowing others to take control of it
				fs.Close();
			}
			catch
			{
			}
			
		} // END OF createPartyMemberID(int totalNumberOfPartyMembers)
	
		/*
			create the PathCoordinates file
			used by the members to locate the leader while on the run (following the leader)
		 */
		public void createPathCoordinates()
		{			
			// create file with desired contents
            pathCoordinatesFS = File.Open(pathCoordinates, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
			pathCoordinatesFS.Close();
			
		} // END OF createPathCoordinates()
	
		/*
			update the PathCoordinates file with the leader's location in the world 
			used by the members to locate the leader while on the run (following the leader)
		 */
		public void updatePathCoordinates(string worldID, string levelAreaID, Vector3 leaderCoordinates)
		{					
			// Convert coordinates (Vector3) into floats, then strings, then separate them by commas
			string coordinates = leaderCoordinates.X.ToString() + "#" + leaderCoordinates.Y.ToString() + "#" + leaderCoordinates.Z.ToString();
			string outputToFile = worldID + "=" + levelAreaID + "=" + coordinates;
			// create file with desired contents
            FileStream fs = File.Open(pathCoordinates, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
			// write the latest coordinates to the file
            using (StreamWriter pathCoordinatesWriter = new StreamWriter(fs))
            {
				// write the leader's current world position to the file
				pathCoordinatesWriter.WriteLine(outputToFile);
			}
			
		//	pauseForABit(2, 3);
			
			fs.Close();
			
		} // END OF createPathCoordinates()
		
		/*
			This method deals with the invites to the party
		 */
        public bool allPresentInParty(int totalNumberOfPartyMembers)
        {
			// access the PartyState file, to see if anybody is still not in the party
			// does file exist?
			if (!File.Exists(partyState))
			{
				// create PartyState file and fill it
				createPartyState(totalNumberOfPartyMembers);
				return false; // if file does not exist, then nobody can be in the party
			}
			try
			{			
				// Open the PartyState file to be read. Allow the party member to write to it
				FileStream fs = File.Open(partyState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using (StreamReader partyStateReader = new StreamReader(fs))
				{		
					// read in the state of Dude1 
					string[] state = partyStateReader.ReadLine().Split('=');		
					if (state[1] == "NotPresent")
					{
						// close the file, allowing others to take control of it
						fs.Close();
						return false; // someone is missing from the party
					}
					
					if (totalNumberOfPartyMembers >= 3)
					{
						// read in the state of Dude2 
						state = partyStateReader.ReadLine().Split('=');
						if (state[1] == "NotPresent")
						{
							// close the file, allowing others to take control of it
							fs.Close();
							return false; // someone is missing from the party
						}
					}
					if (totalNumberOfPartyMembers == 4)
					{
						// read in the state of Dude3 
						state = partyStateReader.ReadLine().Split('=');
						if (state[1] == "NotPresent")
						{
							// close the file, allowing others to take control of it
							fs.Close();
							return false; // someone is missing from the party
						}
					}
					
				}
				// All are present in the party - close the file, allowing others to take control of it
				fs.Close();
			}
			catch
			{
				// probably unable to access the file because it is in use by a follower
				return false; 
			}
			// If we have reached this point, then we have a full party. Everybody has accepted the invite.
			return true; // everybody present and accounted for
			
		} // END OF allPresentInParty()
		
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

