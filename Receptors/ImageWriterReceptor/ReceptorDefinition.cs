﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;

namespace HelloWorldReceptor
{
	public class ReceptorDefinition : IReceptorInstance
	{
		public string Name { get { return "Thumbnail Image Writer"; } }
		public bool IsEdgeReceptor { get { return true; } }
		public bool IsHidden { get { return false; } }

		protected IReceptorSystem rsys;

		public ReceptorDefinition(IReceptorSystem rsys)
		{
			this.rsys = rsys;
		}

		public string[] GetReceiveCarriers()
		{
			return new string[] { "ThumbnailImage" };
		}

		public void Terminate()
		{
		}

		public void ProcessCarrier(ISemanticTypeStruct protocol, dynamic signal)
		{
			string fn = signal.Filename;
			Image img = signal.Image;
			
			// Can't do this because it results in the visualizer throwing an "object is in use elsewhere" exception.
			//Task.Run(() =>
			//	{
			//		lock (img)
			//		{
			//			img.Save(fn);
			//		}
			//	});

			img.Save(fn);
		}
	}
}
