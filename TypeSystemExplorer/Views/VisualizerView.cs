﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Clifton.ExtensionMethods;
using Clifton.Receptor;
using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem;
using Clifton.SemanticTypeSystem.Interfaces;

using TypeSystemExplorer.Controls;
using TypeSystemExplorer.Controllers;
using TypeSystemExplorer.Models;

using Clifton.Tools.Data;

namespace TypeSystemExplorer.Views
{
	class DictionaryPropertyGridAdapter : ICustomTypeDescriptor
	{
		IDictionary _dictionary;

		public DictionaryPropertyGridAdapter(IDictionary d)
		{
			_dictionary = d;
		}
		public string GetComponentName()
		{
			return TypeDescriptor.GetComponentName(this, true);
		}

		public EventDescriptor GetDefaultEvent()
		{
			return TypeDescriptor.GetDefaultEvent(this, true);
		}

		public string GetClassName()
		{
			return TypeDescriptor.GetClassName(this, true);
		}

		public EventDescriptorCollection GetEvents(Attribute[] attributes)
		{
			return TypeDescriptor.GetEvents(this, attributes, true);
		}

		EventDescriptorCollection System.ComponentModel.ICustomTypeDescriptor.GetEvents()
		{
			return TypeDescriptor.GetEvents(this, true);
		}

		public TypeConverter GetConverter()
		{
			return TypeDescriptor.GetConverter(this, true);
		}

		public object GetPropertyOwner(PropertyDescriptor pd)
		{
			return _dictionary;
		}

		public AttributeCollection GetAttributes()
		{
			return TypeDescriptor.GetAttributes(this, true);
		}

		public object GetEditor(Type editorBaseType)
		{
			return TypeDescriptor.GetEditor(this, editorBaseType, true);
		}

		public PropertyDescriptor GetDefaultProperty()
		{
			return null;
		}

		PropertyDescriptorCollection
			System.ComponentModel.ICustomTypeDescriptor.GetProperties()
		{
			return ((ICustomTypeDescriptor)this).GetProperties(new Attribute[0]);
		}

		public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
		{
			ArrayList properties = new ArrayList();
			foreach (DictionaryEntry e in _dictionary)
			{
				properties.Add(new DictionaryPropertyDescriptor(_dictionary, e.Key));
			}

			PropertyDescriptor[] props =
				(PropertyDescriptor[])properties.ToArray(typeof(PropertyDescriptor));

			return new PropertyDescriptorCollection(props);
		}
	}

	class DictionaryPropertyDescriptor : PropertyDescriptor
	{
		IDictionary _dictionary;
		object _key;

		internal DictionaryPropertyDescriptor(IDictionary d, object key)
			: base(key.ToString(), null)
		{
			_dictionary = d;
			_key = key;
		}

		public override Type PropertyType
		{
			get { return _dictionary[_key].GetType(); }
		}

		public override void SetValue(object component, object value)
		{
			_dictionary[_key] = value;
		}

		public override object GetValue(object component)
		{
			return _dictionary[_key];
		}

		public override bool IsReadOnly
		{
			get { return false; }
		}

		public override Type ComponentType
		{
			get { return null; }
		}

		public override bool CanResetValue(object component)
		{
			return false;
		}

		public override void ResetValue(object component)
		{
		}

		public override bool ShouldSerializeValue(object component)
		{
			return false;
		}
	}


	public class FlyoutItem
	{
		public string Text { get; set; }
		public Point Location { get; set; }
		public Size Direction { get; set; }
	}

	public class CarrierAnimationItem
	{
		public Action OnArrivalDo { get; set; }
		public Point StartPosition { get; set; }
		public Rectangle CurrentRegion { get; set; }
		public IReceptorInstance Target { get; set; }
		public ICarrier Carrier { get; set; }
		public int CurveIndex { get; set; }

		public CarrierAnimationItem()
		{
		}
	}

	public class VisualizerView : UserControl
	{
		const int RenderTime = 120;
		const int CarrierTime = 50;
		const int OrbitCountMax = 50;
		protected Size ReceptorSize = new Size(40, 40);
		protected Size ReceptorHalfSize = new Size(20, 20);
		protected Point dropPoint;

		public ApplicationModel Model { get; protected set; }
		public ApplicationFormController ApplicationController { get; protected set; }
		public VisualizerController Controller { get; protected set; }
		public VisualizerControl Visualizer { get; set; }

		public bool StartDrop { get; set; }

		public Point DropPoint
		{
			get { return DropPoint; }
			set { dropPoint = PointToClient(value); }
		}

		protected Dictionary<IReceptor, Point> receptorLocation;
		protected List<FlyoutItem> flyouts;
		protected List<CarrierAnimationItem> carrierAnimations;
		protected Dictionary<IReceptor, List<Image>> carousels;

		protected Brush blackBrush;
		protected Brush whiteBrush;
		protected Pen pen;
		protected Pen whitePen;
		protected Point origin = new Point(0, 0);
		protected Font font;

		protected Random rnd;
		protected Size objectSize = new Size(20, 20);
		protected List<Pen> penColors;
		protected Timer timer;
		
		// Mouse capture / recepture moving fields:
		protected bool moving;
		protected IReceptor selectedReceptor;
		protected Point mouseStart;
		protected Point mousePosition;
		protected DateTime mouseHoverStartTime;

		protected int orbitCount = 0;

		public VisualizerView()
		{
			blackBrush = new SolidBrush(Color.Black);
			whiteBrush = new SolidBrush(Color.White);
			font = new Font(FontFamily.GenericSansSerif, 8);
			pen = new Pen(Color.LightBlue);
			whitePen = new Pen(Color.White);
			penColors = new List<Pen>();

			penColors.Add(new Pen(Color.Red));
			penColors.Add(new Pen(Color.Green));
			penColors.Add(new Pen(Color.Blue));
			penColors.Add(new Pen(Color.Yellow));
			penColors.Add(new Pen(Color.Cyan));
			penColors.Add(new Pen(Color.Magenta));
			penColors.Add(new Pen(Color.Lavender));
			penColors.Add(new Pen(Color.Purple));
			penColors.Add(new Pen(Color.Salmon));

			InitializeCollections();

			rnd = new Random();

			Program.Receptors.NewReceptor += OnNewReceptor;
			Program.Receptors.NewCarrier += OnNewCarrier;

			timer = new Timer();
			timer.Interval = 1000 / 30;		// 30 hz refresh rate in milliseconds;
			timer.Tick += OnTimerTick;
			timer.Start();
		}

		public void Reset()
		{
			InitializeCollections();
			Invalidate(true);
		}

		public void Stop()
		{
			timer.Stop();
			timer.Dispose();
		}

		protected void InitializeCollections()
		{
			receptorLocation = new Dictionary<IReceptor, Point>();
			flyouts = new List<FlyoutItem>();
			carrierAnimations = new List<CarrierAnimationItem>();
			carousels = new Dictionary<IReceptor, List<Image>>();
		}

		public void Flyout(string msg, IReceptorInstance receptorInstance)
		{
			Point p = receptorLocation.Single(kvp => kvp.Key.Instance == receptorInstance).Value;
			flyouts.Add(new FlyoutItem() { Text = msg, Location = p, Direction = new Size(5, -3) });
		}

		public void AnimateCarrier(Action action, IReceptorInstance from, IReceptorInstance to, ICarrier carrier)
		{
			// Assign an unassigned carrier to the target.
			CarrierAnimationItem c = carrierAnimations.SingleOrDefault(q => (q.Carrier == carrier) && (q.Target == null) );

			if (c == null)
			{
				// Oops.  The carrier animation has been consumed, but there are more receptors for this carrier, so we need to add another carrier animation now.
				// Get an existing animation for this carrier.  We don't care what carrier we get, because they're all going to be originating from the same receptor.
				// TODO: This is not necessarily true.  The same carrier could be sourced by different receptors!
				CarrierAnimationItem existing = carrierAnimations.FirstOrDefault(q => (q.Carrier == carrier) && (q.Target != null));

				if (existing != null)
				{
					carrierAnimations.Add(new CarrierAnimationItem() { StartPosition = existing.StartPosition, OnArrivalDo = action, Target = to, Carrier = carrier });
				}
				else
				{
					// else -- hmmm.. Why don't we have an existing carrier animation instance already???
					// We have a carrier that wasn't registered by our event handler, so we have absolutely no clue where it goes.  Place it randomly.
					// These are the CarrierAnimation protocols that we really don't care about.
					// Point p = new Point(rnd.Next(Width), rnd.Next(Height));
					// carrierAnimations.Add(new CarrierAnimationItem() { StartPosition = p, OnArrivalDo = action, Target = to, Carrier = carrier });
				}
			}
			else
			{
				// Use the carrier animation we already have an instance of.
				c.OnArrivalDo = action;
				c.Target = to;
			}
			/*
			if (from == null)
			{
				// From the system edge receptor, so drop the carriers randomly onto the surface.
				Point p = new Point(rnd.Next(Width), rnd.Next(Height));
				carrierAnimations.Add(new CarrierAnimationItem() { StartPosition = p, OnArrivalDo = action, Target = to, Carrier=carrier });
			}
			else
			{
				Point p = receptorLocation.Single(kvp => kvp.Key.Instance == from).Value;
				carrierAnimations.Add(new CarrierAnimationItem() { StartPosition = p, OnArrivalDo = action, Target = to, Carrier = carrier });
			}
			 */
		}

		public void AddImage(IReceptorInstance receptorInstance, Image image)
		{
			IReceptor r = receptorLocation.Single(kvp => kvp.Key.Instance == receptorInstance).Key;
			List<Image> imageList;

			if (!carousels.TryGetValue(r, out imageList))
			{
				imageList = new List<Image>();
				carousels[r] = imageList;
			}

			imageList.Add(image);
			Invalidate(true);
		}

		protected void OnTimerTick(object sender, EventArgs e)
		{
			bool more = Step();

			if (more)
			{
				Visualizer.Refresh();
			}

			CheckMouseHover();
		}

		protected bool Step()
		{
			bool more = ProcessFlyouts();
			more = ProcessCarrierAnimations() | more;			// Always "or" at the end, so that ProcessCarrierAnimations gets called.

			return more;
		}
		
		protected bool ProcessFlyouts()
		{
			bool more = flyouts.Count > 0;

			List<FlyoutItem> remove = new List<FlyoutItem>();

			foreach (FlyoutItem item in flyouts)
			{
				item.Location = Point.Add(item.Location, item.Direction);

				// More to do if any flyout is still onscreen.
				if (!(new Rectangle(new Point(0, 0), Size).Contains(item.Location)))
				{
					remove.Add(item);
				}
			}

			if (remove.Count > 0)
			{
				remove.ForEach(r => flyouts.Remove(r));
			}

			return more;
		}

		protected bool ProcessCarrierAnimations()
		{
			bool more = carrierAnimations.Any(c => c.Target != null);

			List<CarrierAnimationItem> remove = new List<CarrierAnimationItem>();

			// collection can be added by actions, so use an indexer instead
			for (int i=0; i<carrierAnimations.Count; i++)
			{
				CarrierAnimationItem item = carrierAnimations[i];

				if (item.Target != null)
				{
					++item.CurveIndex;

					if (item.CurveIndex == CarrierTime)
					{
						remove.Add(item);
						item.OnArrivalDo();
					}
				}
			}

			if (remove.Count > 0)
			{
				remove.ForEach(r => carrierAnimations.Remove(r));
			}

			return more;
		}

		protected void OnNewReceptor(object sender, NewReceptorEventArgs e)
		{
			// int hw = ctrl.ClientRectangle.Width / 2;
			// int hh = ctrl.ClientRectangle.Height / 2;
			Point p = dropPoint;

			if (!StartDrop)
			{
				p = new Point(rnd.Next(ClientRectangle.Width - 40), rnd.Next(ClientRectangle.Height - 40));
			}

			if (!e.Receptor.Instance.IsHidden)
			{
				receptorLocation[e.Receptor] = p;
				Invalidate(true);
			}
		}

		protected void OnNewCarrier(object sender, NewCarrierEventArgs e)
		{
			// Make sure this isn't a system message, which is a hidden receptor.
			// TODO: We need to check if any receptors exist and whether any are hidden or not.  If it's hidden, then we don't create a carrier animation instance.
			if (!ApplicationController.GetReceiveProtocols().Contains(e.Carrier.Protocol.DeclTypeName))
			{
				if (e.From == null)
				{
					Point p = dropPoint;
					
					if (!StartDrop)
					{
						// From the system edge receptor, so drop the carriers randomly onto the surface.
						p = new Point(rnd.Next(Width), rnd.Next(Height));
					}

					carrierAnimations.Add(new CarrierAnimationItem() { StartPosition = p, Carrier = e.Carrier });
				}
				else
				{
					Point p = receptorLocation.Single(kvp => kvp.Key.Instance == e.From).Value;
					p.Offset((int)(ReceptorSize.Width * Math.Cos(Math.PI * 2.0 * orbitCount / OrbitCountMax)) - 3, (int)(ReceptorSize.Height * Math.Sin(Math.PI * 2.0 * orbitCount / OrbitCountMax)) - 3);
					carrierAnimations.Add(new CarrierAnimationItem() { StartPosition = p, Carrier = e.Carrier });
					orbitCount = (orbitCount + 1) % OrbitCountMax;
				}

				Invalidate(true);
			}
		}

		protected void MouseDownEvent(object sender, MouseEventArgs args)
		{
			if (args.Button == MouseButtons.Left)
			{
				var selectedReceptors = receptorLocation.Where(kvp => (new Rectangle(Point.Subtract(kvp.Value, ReceptorHalfSize), ReceptorSize)).Contains(args.Location));

				if (selectedReceptors.Count() > 0)
				{
					selectedReceptor = selectedReceptors.First().Key;
					moving = true;
					mouseStart = args.Location;
				}
			}
		}

		protected void MouseUpEvent(object sender, MouseEventArgs args)
		{
			moving = false;

			if ((selectedReceptor != null) && (!ClientRectangle.Contains(args.Location)) )
			{
				// Remove the receptor.
				Program.Receptors.Remove(selectedReceptor);
				receptorLocation.Remove(selectedReceptor);
				carrierAnimations.RemoveAll(a => a.Target == selectedReceptor.Instance);
			}

			selectedReceptor = null;
		}

		protected void MouseMoveEvent(object sender, MouseEventArgs args)
		{
			mousePosition = args.Location;

			if (moving)
			{
				base.OnMouseMove(args);
				Point offset = Point.Subtract(args.Location, new Size(mouseStart));
				Point curPos = receptorLocation[selectedReceptor];
				receptorLocation[selectedReceptor] = Point.Add(curPos, new Size(offset));
				mouseStart = args.Location;
				Invalidate(true);
			}

			mouseHoverStartTime = DateTime.Now;
		}

		/// <summary>
		/// The .NET MouseHoverEvent is f*cked.  It will not re-trigger until the mouse moves outside of the control.
		/// Even that seems problematic.  So we have a manual implementation.
		/// </summary>
		protected void CheckMouseHover()
		{
			if ((DateTime.Now - mouseHoverStartTime).TotalMilliseconds > 500)
			{
				CarrierAnimationItem item = carrierAnimations.FirstOrDefault(a => a.CurrentRegion.Contains(mousePosition));

				// Mouse is hovering over a carrier.
				if (item != null)
				{
					// Use the properties window to reveal the carrier contents.
					// The property grid is sort of stupid, so we'll put together an anonymous object for displaying the carrier protocol and signal.

					// new { Protocol = item.Carrier.Protocol.DeclTypeName, from t in Program.SemanticTypeSystem.GetSemanticTypeStruct(item.Carrier.Protocol.DeclTypeName).NativeTypes select new {Name = t.Name, Value = t.GetValue(item.Carrier.Signal)}};
					// var obj = (from t in Program.SemanticTypeSystem.GetSemanticTypeStruct(item.Carrier.Protocol.DeclTypeName).NativeTypes select new { Name = t.Name, Value = t.GetValue(item.Carrier.Signal) }).First();
					// var obj = new { Protocol = item.Carrier.Protocol.DeclTypeName, Signal = item.Carrier.Signal };
					// var obj = new CarrierProperty(item.Carrier.Protocol.DeclTypeName, item.Carrier.Signal);

					// There probably is a better way to do this:
					IDictionary dict = new Hashtable();
					dict.Add("Protocol", item.Carrier.Protocol.DeclTypeName);
					var kvpList = (from t in Program.SemanticTypeSystem.GetSemanticTypeStruct(item.Carrier.Protocol.DeclTypeName).NativeTypes select new { Name = t.Name, Value = t.GetValue(item.Carrier.Signal) }); // .ForEach((item) =
					kvpList.ForEach((kvp) =>
						{
							dict.Add(kvp.Name, kvp.Value);
						});
					ApplicationController.PropertyGridController.ShowObject(new DictionaryPropertyGridAdapter(dict));
				}

				// Reset so that this test is not made again until the mouse is moved.
				mouseHoverStartTime = DateTime.MaxValue;
			}
		}

		protected void OnVisualizerPaint(object sender, PaintEventArgs e)
		{
			try
			{
				Control ctrl = (Control)sender;

				e.Graphics.FillRectangle(blackBrush, new Rectangle(Location, Size));
				e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

				receptorLocation.ForEach(kvp =>
					{
						Pen pen = penColors[0];
						Point p = kvp.Value;
						p.Offset(-ReceptorSize.Width / 2, -ReceptorSize.Height / 2);
						Point bottom = p;
						bottom.Offset(0, ReceptorSize.Height);
						Point bottomCenter = bottom;
						bottomCenter.Offset(ReceptorSize.Width / 2, 0);

						// Double plot because it looks better.
						e.Graphics.DrawEllipse(pen, new Rectangle(p, ReceptorSize));
						e.Graphics.DrawEllipse(pen, new Rectangle(p, ReceptorSize));

						// A double circle represents an edge receptor.
						if (kvp.Key.Instance.IsEdgeReceptor)
						{
							p.Offset(3, 3);		// GDI draws from the UL corner.
							Size s = Size.Subtract(ReceptorSize, new Size(6, 6));
							e.Graphics.DrawEllipse(pen, new Rectangle(p, s));
							e.Graphics.DrawEllipse(pen, new Rectangle(p, s));
						}

						SizeF strSize = e.Graphics.MeasureString(kvp.Key.Name, font);
						Point center = Point.Subtract(bottomCenter, new Size((int)strSize.Width / 2, 0));
						e.Graphics.DrawString(kvp.Key.Name, font, whiteBrush, center);
					});

				flyouts.ForEach(f =>
					{
						e.Graphics.DrawString(f.Text, font, whiteBrush, f.Location);
					});

				// Show carriers with targets.
				carrierAnimations.Where(q => q.Target != null).ForEach(a =>
					{
						// Get current target location in case user has moved it.
						Point p = receptorLocation.Single(kvp => kvp.Key.Instance == a.Target).Value;
						double dx = p.X - a.StartPosition.X;
						double dy = p.Y - a.StartPosition.Y;

						// Where are we on the curve?
						double q = Math.Sin((Math.PI / 2) * ((double)a.CurveIndex - CarrierTime / 2) / (CarrierTime / 2)) + 1;		// - PI/2 .. PI/2
						int idx = (int)(dx * q / 2.0);
						int idy = (int)(dy * q / 2.0);

						a.CurrentRegion = new Rectangle(a.StartPosition.X + idx - 5, a.StartPosition.Y + idy - 5, 10, 10);

						Point[] triangle = new Point[] 
					{ 
						new Point(a.StartPosition.X + idx, a.StartPosition.Y + idy), 
						new Point(a.StartPosition.X + idx - 5, a.StartPosition.Y + idy + 5), 
						new Point(a.StartPosition.X + idx + 5, a.StartPosition.Y + idy + 5),
						new Point(a.StartPosition.X + idx, a.StartPosition.Y + idy), 
					};

						e.Graphics.DrawLines(penColors[3], triangle);
					});

				// Show carriers without targets.
				carrierAnimations.Where(q => q.Target == null).ForEach(a =>
					{
						double dx = a.StartPosition.X;
						double dy = a.StartPosition.Y;

						// Where are we on the curve?
						double q = Math.Sin((Math.PI / 2) * ((double)a.CurveIndex - CarrierTime / 2) / (CarrierTime / 2)) + 1;		// - PI/2 .. PI/2
						int idx = (int)(dx * q / 2.0);
						int idy = (int)(dy * q / 2.0);

						a.CurrentRegion = new Rectangle(a.StartPosition.X + idx - 5, a.StartPosition.Y + idy - 5, 10, 10);

						Point[] triangle = new Point[] 
					{ 
						new Point(a.StartPosition.X + idx, a.StartPosition.Y + idy), 
						new Point(a.StartPosition.X + idx - 5, a.StartPosition.Y + idy + 5), 
						new Point(a.StartPosition.X + idx + 5, a.StartPosition.Y + idy + 5),
						new Point(a.StartPosition.X + idx, a.StartPosition.Y + idy), 
					};

						e.Graphics.DrawLines(penColors[3], triangle);
					});

				carousels.ForEach(kvp =>
					{
						Point p = receptorLocation[kvp.Key];
						// p.Offset(-ReceptorSize.Width / 2, -ReceptorSize.Height / 2);
						double images = kvp.Value.Count;

						kvp.Value.ForEachWithIndex((img, idx) =>
						{
							Point ip = p;
							double dx = 150 * Math.Cos(2 * Math.PI * idx / images);
							double dy = 150 * Math.Sin(2 * Math.PI * idx / images);
							ip.Offset((int)dx, (int)dy);
							e.Graphics.DrawImage(img, new Rectangle(new Point(ip.X - 50, ip.Y - 50 * img.Height / img.Width), new Size(100, 100 * img.Height / img.Width)));
						});
					});
			}
			catch (Exception ex)
			{
				// System.Diagnostics.Debugger.Break();
			}
		}
	}
}
