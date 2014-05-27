﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;

namespace LoggerReceptor
{
	/// <summary>
	/// This receptor is an edge receptor, receiving DebugMessage carriers and outputting them to a logging window.
	/// </summary>
	public class ReceptorDefinition : IReceptorInstance
	{
		public string Name { get { return "Logger"; } }
		public bool IsEdgeReceptor { get { return true; } }
		public bool IsHidden { get { return false; } }

		protected IReceptorSystem rsys;

		public ReceptorDefinition(IReceptorSystem rsys)
		{
			this.rsys = rsys;
		}

		public string[] GetReceiveProtocols()
		{
			return new string[] { "DebugMessage" };
		}

		public void Terminate()
		{
		}

		public void ProcessCarrier(ISemanticTypeStruct protocol, dynamic signal)
		{
			if (protocol.DeclTypeName == "DebugMessage")
			{
				string msg = signal.Message;
				System.Diagnostics.Debug.WriteLine(msg);

				Flyout(msg);
			}
		}

		/// <summary>
		/// A visualization at the system level.
		/// </summary>
		/// <param name="msg"></param>
		protected void Flyout(string msg)
		{
			ISemanticTypeStruct protocol = rsys.SemanticTypeSystem.GetSemanticTypeStruct("SystemMessage");
			dynamic signal = rsys.SemanticTypeSystem.Create("SystemMessage");
			signal.Action = "Flyout";
			signal.Data = msg;
			signal.Source = this;
			rsys.CreateCarrier(this, protocol, signal);
		}
	}
}
