﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
// using System.Threading.Tasks;

using Clifton.Tools.Strings.Extensions;
using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;

namespace ThumbnailCreatorReceptor
{
	public class ReceptorDefinition : IReceptorInstance
	{
		public string Name { get { return "Thumbnail Converter"; } }
		public bool IsEdgeReceptor { get { return false; } }
		public bool IsHidden { get { return false; } }

		protected IReceptorSystem rsys;

		public ReceptorDefinition(IReceptorSystem rsys)
		{
			this.rsys = rsys;
		}

		public string[] GetReceiveProtocols()
		{
			return new string[] { "ImageFilename" };
		}

		public void Initialize()
		{
		}

		public void Terminate()
		{
		}

		// was public void async...
		public void ProcessCarrier(ICarrier carrier)
		{
			if (carrier.Signal.Filename != null)
			{
				string fn = carrier.Signal.Filename;

				// Only process if the file exists.
				if (File.Exists(fn))
				{
					// This is fast enough we don't need to run this as a separate thread unless these files are perhaps coming from a slow network.
					Bitmap bitmap = new Bitmap(fn);
					// Reduce the size of the image.  If we don't do this, scrolling and rendering of scaled images is horrifically slow.
					Image image = new Bitmap(bitmap, 256, 256 * bitmap.Height / bitmap.Width);
					image.Tag = fn;
					bitmap.Dispose();
					OutputImage(fn, image);
				}
				else
				{
					FileMissing(fn);
				}
			}
			else
			{
				NoFilenameProvided();
			}

/*
			Image ret = await Task.Run<Image>(() =>
				{
					Bitmap bitmap = new Bitmap(fn);
					// Reduce the size of the image.  If we don't do this, scrolling and rendering of scaled images is horrifically slow.
					Image image = new Bitmap(bitmap, 256, 256 * bitmap.Height / bitmap.Width);
					image.Tag = fn;
					bitmap.Dispose();

					return image;
				});
			OutputImage(fn, ret);
*/
		}

		protected void FileMissing(string fn)
		{
			ISemanticTypeStruct protocol = rsys.SemanticTypeSystem.GetSemanticTypeStruct("DebugMessage");
			dynamic signal = rsys.SemanticTypeSystem.Create("DebugMessage");
			signal.Message = "Thumbnail Converter: The image file "+fn+" is missing!";
			rsys.CreateCarrier(this, protocol, signal);
		}

		protected void NoFilenameProvided()
		{
			ISemanticTypeStruct protocol = rsys.SemanticTypeSystem.GetSemanticTypeStruct("DebugMessage");
			dynamic signal = rsys.SemanticTypeSystem.Create("DebugMessage");
			signal.Message = "Thumbnail Converter: No image filename was provided.";
			rsys.CreateCarrier(this, protocol, signal);
		}

		protected void OutputImage(string filename, Image image)
		{
			ISemanticTypeStruct protocol = rsys.SemanticTypeSystem.GetSemanticTypeStruct("ThumbnailImage");
			dynamic signal = rsys.SemanticTypeSystem.Create("ThumbnailImage");
			// Insert "-thumbnail" into the filename.
			signal.ImageFilename.Filename = filename.LeftOfRightmostOf('.') + "-thumbnail." + filename.RightOfRightmostOf('.');
			image.Tag = signal.ImageFilename.Filename;
			signal.Image = image;
			rsys.CreateCarrierIfReceiver(this, protocol, signal);
		}
	}
}
