﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;

namespace HelloWorldReceptor
{
	public class ReceptorDefinition : IReceptorInstance
	{
#pragma warning disable 67
		public event EventHandler<EventArgs> ReceiveProtocolsChanged;
		public event EventHandler<EventArgs> EmitProtocolsChanged;
#pragma warning restore 67

		public string Name { get { return "Thumbnail Image Writer"; } }
		public bool IsEdgeReceptor { get { return true; } }
		public bool IsHidden { get { return false; } }

		public IReceptorSystem ReceptorSystem
		{
			get { return rsys; }
			set { rsys = value; }
		}

		protected IReceptorSystem rsys;

		public ReceptorDefinition(IReceptorSystem rsys)
		{
			this.rsys = rsys;
		}

		public string[] GetReceiveProtocols()
		{
			return new string[] { "ThumbnailImage" };
		}

		public string[] GetEmittedProtocols()
		{
			return new string[] { };
		}

		public void Initialize()
		{
		}

		public void Terminate()
		{
		}

		public void ProcessCarrier(ICarrier carrier)
		{
			string fn = carrier.Signal.ImageFilename.Filename;

			// Only save the file if it doesn't already exists.  
			// In actual usage, with this receptor online, we can write a lot of duplicate thumbnails!
			if (!File.Exists(fn))
			{
				Image img = carrier.Signal.Image;

				// Can't do this because it results in the visualizer throwing an "object is in use elsewhere" exception.
				//Task.Run(() =>
				//	{
				//		lock (img)
				//		{
				//			img.Save(fn);
				//		}
				//	});

				// TODO: Make sure we save the image in the filename specified by the extension.
				img.Save(fn, ImageFormat.Jpeg);
			}
		}
	}
}
