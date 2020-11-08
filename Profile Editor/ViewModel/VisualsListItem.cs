//  Copyright 2014 Craig Courtney
//  Copyright 2020 Helios Contributors
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

namespace GadrocsWorkshop.Helios.ProfileEditor.ViewModel
{
    public class VisualsListItem : NotificationObject
    {
        private bool _selected;

        public VisualsListItem(HeliosVisual control, bool isSelected)
        {
            Control = control;
            _selected = isSelected;
        }

        public HeliosVisual Control { get; }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected.Equals(value))
                {
                    return;
                }

                bool oldValue = _selected;
                _selected = value;
                OnPropertyChanged("IsSelected", oldValue, value, false);
            }
        }
    }
}
