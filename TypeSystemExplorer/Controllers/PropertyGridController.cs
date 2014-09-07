﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Clifton.ExtensionMethods;
using Clifton.SemanticTypeSystem;

using TypeSystemExplorer.Views;

namespace TypeSystemExplorer.Controllers
{
	public class PropertyGridController : ViewController<PropertyGridView>
	{
		protected void Opening()
		{
		}

		protected void Closing()
		{
		}

		public void ShowObject(object obj)
		{
			if (obj is KeyValuePair<string, SemanticType>)
			{
				View.ShowObject(((KeyValuePair<string, SemanticType>)obj).Value);
			}
			else
			{
				View.ShowObject(obj);
			}
		}

		/// <summary>
		/// Updates the XML node with the name set in the property grid.
		/// </summary>
		protected void OnPropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
		{
			// Annoyingly, the property grid change notifier doesn't give us the property name, it gives us the display name for the property being changed.
			// TODO: We might be better off trying to figure out how to wire up an event for the Name property when instances are created.
			if ( (e.ChangedItem.Label == "Name") || (e.ChangedItem.Label=="Semantic Type") )
			{
				ApplicationController.SemanticTypeEditorController.IfNotNull((ctrl) =>
					{
						ctrl.UpdateNodeText(e.ChangedItem.Value.ToString());
					});
			}
		}
	}
}
