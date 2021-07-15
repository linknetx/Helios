//  Copyright 2014 Craig Courtney
//    
//  Helios is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  Helios is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

namespace GadrocsWorkshop.Helios.Controls
{
	using GadrocsWorkshop.Helios.Interfaces.Falcon;
	using System;
	using System.Xml;

	public class MapControlOverlayImages : HeliosInterface
	{
		private FalconInterface _falconInterface;

		private string[,] _waypointImages_015 = new string[20, 2];
		private string[,] _waypointImages_030 = new string[20, 2];
		private string[,] _waypointImages_060 = new string[20, 2];
		private string[,] _waypointImages_120 = new string[20, 2];
		private const string _targetImage_015 = "{HeliosFalcon}/Images/Waypoints/TGT_015.png";
		private const string _targetImage_030 = "{HeliosFalcon}/Images/Waypoints/TGT_030.png";
		private const string _targetImage_060 = "{HeliosFalcon}/Images/Waypoints/TGT_060.png";
		private const string _targetImage_120 = "{HeliosFalcon}/Images/Waypoints/TGT_120.png";

		private bool _profileStarted = false;


		public MapControlOverlayImages()
			: base("MapControl")
		{
			InitializeWaypointImageArrays();
		}


		void InitializeWaypointImageArrays()
		{
			for (int i = 0; i < _waypointImages_015.GetLength(0); i++)
			{
				_waypointImages_015[i, 0] = "{HeliosFalcon}/Images/Waypoints/WPT_BLK_015_" + i.ToString("D2") + ".png";
				_waypointImages_015[i, 1] = "{HeliosFalcon}/Images/Waypoints/WPT_RED_015_" + i.ToString("D2") + ".png";
			}

			for (int i = 0; i < _waypointImages_030.GetLength(0); i++)
			{
				_waypointImages_030[i, 0] = "{HeliosFalcon}/Images/Waypoints/WPT_BLK_030_" + i.ToString("D2") + ".png";
				_waypointImages_030[i, 1] = "{HeliosFalcon}/Images/Waypoints/WPT_RED_030_" + i.ToString("D2") + ".png";
			}

			for (int i = 0; i < _waypointImages_060.GetLength(0); i++)
			{
				_waypointImages_060[i, 0] = "{HeliosFalcon}/Images/Waypoints/WPT_BLK_060_" + i.ToString("D2") + ".png";
				_waypointImages_060[i, 1] = "{HeliosFalcon}/Images/Waypoints/WPT_RED_060_" + i.ToString("D2") + ".png";
			}

			for (int i = 0; i < _waypointImages_120.GetLength(0); i++)
			{
				_waypointImages_120[i, 0] = "{HeliosFalcon}/Images/Waypoints/WPT_BLK_120_" + i.ToString("D2") + ".png";
				_waypointImages_120[i, 1] = "{HeliosFalcon}/Images/Waypoints/WPT_RED_120_" + i.ToString("D2") + ".png";
			}
		}

		protected override void OnProfileChanged(HeliosProfile oldProfile)
		{
			base.OnProfileChanged(oldProfile);

			if (oldProfile != null)
			{
				oldProfile.ProfileStarted -= new EventHandler(Profile_ProfileStarted);
				oldProfile.ProfileTick -= new EventHandler(Profile_ProfileTick);
				oldProfile.ProfileStopped -= new EventHandler(Profile_ProfileStopped);
			}

			if (Profile != null)
			{
				Profile.ProfileStarted += new EventHandler(Profile_ProfileStarted);
				Profile.ProfileTick += new EventHandler(Profile_ProfileTick);
				Profile.ProfileStopped += new EventHandler(Profile_ProfileStopped);
			}
		}

		void Profile_ProfileStarted(object sender, EventArgs e)
		{
			if (_falconInterface.Profile.Interfaces.ContainsKey("Falcon"))
			{
				_falconInterface = _falconInterface.Profile.Interfaces["Falcon"] as FalconInterface;
			}
		}

		void Profile_ProfileTick(object sender, EventArgs e)
		{
			if (_falconInterface != null)
			{
				//  if string data new
				//  ProcessDataValues();
			}
		}

		void Profile_ProfileStopped(object sender, EventArgs e)
		{
			_falconInterface = null;
		}

		public override void ReadXml(XmlReader reader)
		{
			// No-Op
		}

		public override void WriteXml(XmlWriter writer)
		{
			// No-Op
		}
	}
}
