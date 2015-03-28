/*
    Copyright 2104 Higher Order Programming

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Clifton.ExtensionMethods;
using Clifton.Receptor.Interfaces;
using Clifton.SemanticTypeSystem.Interfaces;

namespace Clifton.Receptor
{
	/// <summary>
	/// Internal class for managing the queued carrier and the action to carry out when 
	/// a receptor receiving the carrier's protocol becomes available.
	/// </summary>
	public class QueuedCarrierAction
	{
		public IReceptor From { get; set; }
		public Carrier Carrier { get; set; }
		public Action Action { get; set; }
	}

	/// <summary>
	/// Container for all registered receptors, as well as providing managing the creation
	/// of carriers and the execution of a carrier's protocol on receiving receptors.
	/// </summary>
    public class ReceptorsContainer : IReceptorSystem
    {
		/// <summary>
		/// Fires when a new receptor is instantiated and registered into the system.
		/// </summary>
		public event EventHandler<ReceptorEventArgs> NewReceptor;

		/// <summary>
		/// Fires when a new carrier is created.
		/// </summary>
		public event EventHandler<NewCarrierEventArgs> NewCarrier;

		/// <summary>
		/// Fires when a receptor is removed.
		/// </summary>
		public event EventHandler<ReceptorEventArgs> ReceptorRemoved;

		/// <summary>
		/// The collection of receptors currently in the system.
		/// </summary>
		// public List<Receptor> Receptors { get; protected set; }
		// Yuck.  Way to much conversion going on here.
		public ReadOnlyCollection<IReceptor> Receptors { get { return receptors.Cast<IReceptor>().ToList().AsReadOnly(); } }

		/// <summary>
		/// The Semantic Type System instance that defines the carrier protocols.
		/// </summary>
		public ISemanticTypeSystem SemanticTypeSystem { get; protected set; }

		/// <summary>
		/// The list of receptors to which each protocol maps.
		/// This list supports unmapped "from receptors" in the registered receptor map,
		/// allowing the "system" to drop carriers into a particular membrane and have that
		/// carrier be processed by a receive-only receptor in the membrane.
		/// </summary>
		protected Dictionary<string, List<IReceptorConnection>> protocolReceptorMap;

		/// <summary>
		/// A map of registered receptors.  These are receptors that are instantiated
		/// and whose protocols have been associated in the protocolReceptorMap.
		/// </summary>
		protected  Dictionary<IReceptor, bool> registeredReceptorMap;

		/// <summary>
		/// Internal list of global receptors, which are handled slightly differently
		/// as they receive all carriers.
		/// </summary>
		protected List<IReceptor> globalReceptors;

		/// <summary>
		/// The list of queued carriers because there are no receptors available to process the 
		/// carrier's protocol.
		/// </summary>
		private List<QueuedCarrierAction> queuedCarriers;

		/// <summary>
		/// Exposed to make unit testing easier for receptors that emit carriers that aren't processed.
		/// </summary>
		public List<QueuedCarrierAction> QueuedCarriers { get { return queuedCarriers; } }

		/// <summary>
		/// Internal collection of receptors in this receptor system.
		/// </summary>
		protected List<Receptor> receptors;

		// TODO: The receptors container should acquire this list, rather than it being set.
		/// <summary>
		/// The master list of receptor connections, which oddly at this point is being computed
		/// by the visualizer.
		/// </summary>
		public Dictionary<IReceptor, List<IReceptorConnection>> MasterReceptorConnectionList { get; set; }

		public IReceptor this[string name] { get { return receptors.Single(r => r.Name == name); } }

		public IMembrane Membrane { get; set; }

		/// <summary>
		/// Constructor, initializes internal collections.
		/// </summary>
		public ReceptorsContainer(ISemanticTypeSystem sts, IMembrane membrane = null)
		{
			Membrane = membrane;
			SemanticTypeSystem = sts;
			Initialize();
		}

		/// <summary>
		/// Adds a receptor definition to the collection.  Call this method when mass-loading
		/// receptors with their names and implementing assembly names, say from an XML file.
		/// </summary>
		public Receptor RegisterReceptor(string receptorName, string assemblyName)
		{
			Receptor r = new Receptor(receptorName, assemblyName);
			r.EnabledStateChanged += WhenEnabledStateChanged;
			receptors.Add(r);

			return r;
		}

		/// <summary>
		/// Add an existing implementor of IReceptorInstance.  Call this method when registering
		/// application-level instances that are themselves receptors, such as models, controllers,
		/// views, etc.
		/// </summary>
		public void RegisterReceptor(string name, IReceptorInstance inst)
		{
			Receptor r = new Receptor(name, inst);
			r.EnabledStateChanged += WhenEnabledStateChanged;
			receptors.Add(r);
		}

		/// <summary>
		/// Register a receptor given just the assembly filename.  The receptor assembly must still be loaded
		/// by calling LoadReceptors.  Used for registering receptors in a batch before instantiating them.
		/// </summary>
		/// <param name="filename"></param>
		public IReceptor RegisterReceptor(string filename)
		{
			Receptor r = Receptor.FromFile(filename).Instantiate(this);
			receptors.Add(r);

			return r;
		}

		/// <summary>
		/// When all current receptors have been registered (see Register... methods), call this method to
		/// instantiate (if necessary) and register the receptors.  This also generates the 
		/// protocol-receptor map for the currently registered receptors.
		/// </summary>
		public void LoadReceptors(Action<IReceptor> afterRegister = null)
		{
			int processedCount = 0;
			string receptorName;
			List<Receptor> newReceptors = new List<Receptor>();

			// TODO: Refactor out of this code.
			Say("Loading receptors.");

			foreach (Receptor r in Receptors)
			{
				// TODO: This is lame, to prevent existing receptors from being re-processed.
				if (!r.Instantiated)
				{
					r.LoadAssembly().Instantiate(this);
				}

				// Get the carriers this receptor is interested in.
				if (!registeredReceptorMap.ContainsKey(r))
				{
					registeredReceptorMap[r] = true;

					// Get the protocols that this receptor is receiving, updating the protocolReceptorMap, adding this
					// receptor to the specified protocols.
					GatherProtocolReceivers(r);
					newReceptors.Add(r);
					++processedCount;
					receptorName = r.Name;
				}
			}

			// This order is important.  The visualizer needs to know all the receptors within this membrane AFTER
			// the receptors have been instantiated.  Secondly, the receptors can't be initialized until the visualizer
			// knows where they are.

			// Let interested parties know that we have new receptors and handle how we want to announce the fact.
			newReceptors.ForEach(r =>
			{
				if (afterRegister != null)
				{
					afterRegister(r);
				}

				NewReceptor.Fire(this, new ReceptorEventArgs(r));
			});

			// Let the receptor instance perform additional initialization, such as creating carriers.
			// We do this only for enabled receptors.
			newReceptors.Where(r=>r.Enabled).ForEach(r => r.Instance.Initialize());

			// Any queued carriers are now checked to determine whether receptors now exist to process their protocols.
			ProcessQueuedCarriers(true);

			// If we've loaded only one receptor...
			// TODO: Refactor this out of here.
			if (processedCount == 1)
			{
				Say("Receptor " + Receptors[0].Name + " online.");
			}
			else
			{
				Say("Receptors online.");
			}
		}

		/// <summary>
		/// Kludge to get protocols ending with "Recordset".
		/// </summary>
		public List<string> GetProtocolsEndingWith(string match)
		{
			List<string> ret = new List<string>();

			ret.AddRange(SemanticTypeSystem.SemanticTypes.Keys.Where(k => k.EndsWith(match)));

			return ret;
		}

		/// <summary>
		/// Create a carrier of the specified protocol and signal.
		/// </summary>
		/// <param name="from">The source receptor.  Cay be null.</param>
		/// <param name="protocol">The protocol.</param>
		/// <param name="signal">The signal in the protocol's format.</param>
		public ICarrier CreateCarrier(IReceptorInstance from, ISemanticTypeStruct protocol, dynamic signal, ICarrier parentCarrier = null, bool emitSubElements = true)
		{
			// This calls the internal method with recursion set to false.  We don't want to expose this 
			// flag, so this method is a public front, as receptors should never set the "stop recursion" flag
			// to true when creating carriers.
			// TODO: Improve these "is it a system message" tests -- figure out how to get rid of these hardcoded string.
			ICarrier carrier = CreateCarrier(from, protocol, protocol.DeclTypeName, signal, false, protocol.DeclTypeName=="SystemMessage" || from.Name=="DropReceptor", parentCarrier, emitSubElements);

			return carrier;
		}

		/// <summary>
		/// Create a carrier of the specified protocol and signal if a receiver currently exists.
		/// Note that because a carrier might not be created, there is no return value for this method.
		/// </summary>
		/// <param name="from">The source receptor.  Cay be null.</param>
		/// <param name="protocol">The protocol.</param>
		/// <param name="signal">The signal in the protocol's format.</param>
		public void CreateCarrierIfReceiver(IReceptorInstance from, ISemanticTypeStruct protocol, dynamic signal, ICarrier parentCarrier = null, bool emitSubElements = true)
		{
			CreateCarrierIfReceiver(from, protocol, protocol.DeclTypeName, signal, parentCarrier, emitSubElements);
		}

		protected void CreateCarrierIfReceiver(IReceptorInstance from, ISemanticTypeStruct protocol, string protocolPath, dynamic signal, ICarrier parentCarrier, bool emitSubElements = true)
		{
			if (from.GetEnabledEmittedProtocols().Any(p => p.Protocol == protocol.DeclTypeName))
			{
				if (TargetReceptorExistsFor(ReceptorFromInstance(from), protocol, protocol.DeclTypeName == protocolPath))
				{
					// This call will recurse for us.
					// CreateCarrier(IReceptorInstance from, ISemanticTypeStruct protocol, string protocolPath, dynamic signal, bool stopRecursion, bool isSystemMessage = false, ICarrier parentCarrier = null)
					CreateCarrier(from, protocol, protocolPath, signal, false, false, parentCarrier, emitSubElements, protocol.DeclTypeName == protocolPath);
				}
				else
				{
					if (emitSubElements)
					{
						// Recurse into SE's of the protocol and emit carriers for those as well, if a receiver exists.
						// We do this even if there isn't a target for the top-level receptor.
						// However, we do create a Carrier instance as the parent so the child can reference it if necessary.
						Carrier carrier = new Carrier(protocol, protocolPath, signal, parentCarrier);
						CreateCarriersForSemanticElements(from, protocol, protocolPath, signal, false, carrier);
					}
				}
			}
			else
			{
				if (emitSubElements)
				{
					// Create a Carrier instance as the parent so the child can reference it if necessary.
					Carrier carrier = new Carrier(protocol, protocolPath, signal, parentCarrier);
					CreateCarriersForSemanticElements(from, protocol, protocolPath, signal, false, carrier);
				}
			}
		}

		/// <summary>
		/// Removes a receptor from the system.  Before being removed, the receptor's Terminate() 
		/// method is called so that it can do whatever cleanup is required.
		/// </summary>
		/// <param name="receptor"></param>
		public void Remove(IReceptor receptor)
		{
			receptor.Instance.Terminate();
			// TODO: If our collections were IReceptor, then we wouldn't need the "as".
			receptors.Remove(receptor as Receptor);
			registeredReceptorMap.Remove(receptor);

			protocolReceptorMap.ForEach(kvp =>
			{
				IReceptorConnection conn = kvp.Value.SingleOrDefault(rc => rc.Receptor == receptor);
				conn.IfNotNull(c => kvp.Value.Remove(c));
			});

			ReceptorRemoved.Fire(this, new ReceptorEventArgs(receptor));

			// TODO: Refactor out of this code.
			// TODO: Add a "RemovedReceptor" event.
			Say("Receptor " + receptor.Name + " removed.");
		}

		/// <summary>
		/// Call on each receptor instance when the system has been fully initialized (applet or a receptor is dropped onto the surface.)
		/// </summary>
		public void EndSystemInit()
		{
			receptors.ForEach(r => r.Instance.EndSystemInit());
		}

		/// <summary>
		/// Remove the receptor container of the specified instance.
		/// </summary>
		/// <param name="receptorInstance"></param>
		public void Remove(IReceptorInstance receptorInstance)
		{
			// Clone the list because the master list will change.
			Remove((Receptor)ReceptorFromInstance(receptorInstance));
		}

		/// <summary>
		/// Reset the receptor system.  This allows the current carriers to cleanly terminate
		/// and then re-initializes the internal collections to an empty state.
		/// </summary>
		public void Reset()
		{
			Receptors.ForEach(r => r.Instance.Terminate());
			Initialize();
		}

		// TODO: Re-implement by providing methods to add/remove instantiated receptors which
		// would support moving receptors between membranes.  This is a brute force approach
		// for now.
		/// <summary>
		/// Recreates the protocol receptor map from scratch.
		/// </summary>
		public void ReloadProtocolReceptorMap()
		{
			protocolReceptorMap.Clear();
			Receptors.ForEach(r => GatherProtocolReceivers(r));
		}

		public void MoveReceptorTo(IReceptor receptor, ReceptorsContainer target)
		{
			InternalRemove(receptor);
			target.InternalAdd(receptor);
		}

		/// <summary>
		/// Remove the receptor without generating remove events or terminating the receptor.
		/// </summary>
		protected void InternalRemove(IReceptor receptor)
		{
			receptors.Remove(receptor as Receptor);
			registeredReceptorMap.Remove(receptor);

			protocolReceptorMap.ForEach(kvp =>
				{
					IReceptorConnection conn = kvp.Value.SingleOrDefault(rc => rc.Receptor == receptor);
					conn.IfNotNull(c=>kvp.Value.Remove(c));
				});
		}

		/// <summary>
		/// Add the receptor, without generating add events.
		/// </summary>
		protected void InternalAdd(IReceptor receptor)
		{
			// TODO: If receptors was List<IReceptor>, we wouldn't need this cast.
			receptors.Add(receptor as Receptor);
			registeredReceptorMap[receptor] = true;
			// The receptor instance is now using this receptor system!
			receptor.Instance.ReceptorSystem = this;
			// Process any queued carriers that may now become active.
			ReloadProtocolReceptorMap();
			ProcessQueuedCarriers(true);
		}

		/// <summary>
		/// Clears out all data.
		/// </summary>
		protected void Initialize()
		{
			receptors = new List<Receptor>();
			protocolReceptorMap = new Dictionary<string, List<IReceptorConnection>>();
			queuedCarriers = new List<QueuedCarrierAction>();
			globalReceptors = new List<IReceptor>();
			registeredReceptorMap = new Dictionary<IReceptor, bool>();
			MasterReceptorConnectionList = new Dictionary<IReceptor, List<IReceptorConnection>>();
		}

		/// <summary>
		/// Get the protocols that this receptor is receiving, updating the protocolReceptorMap, adding this
		/// receptor to the specified protocols.
		/// </summary>
		protected void GatherProtocolReceivers(IReceptor r)
		{
			// For each protocol...
			r.Instance.GetReceiveProtocols().ForEach(rq =>
				{
					// If it's a wildcard receptor, we handle it differently because "*" is a magic protocol.
					if (rq.Protocol == "*")
					{
						// This is a global receiver.  Attach it to all current carrier receptors, but don't create an instance in the CarrierReceptorMap.
						protocolReceptorMap.ForEach(kvp => kvp.Value.Add(new ReceptorConnection(r)));
						globalReceptors.Add(r);
					}
					else
					{
						// Get the list of receiving receptors for the protocol, or, if it doesn't exist, create it.
						List<IReceptorConnection> receivingReceptors;

						if (!protocolReceptorMap.TryGetValue(rq.Protocol, out receivingReceptors))
						{
							receivingReceptors = new List<IReceptorConnection>();
							protocolReceptorMap[rq.Protocol] = receivingReceptors;
							// Append all current global receptors to this protocol - receptor map.
							globalReceptors.ForEach(gr => receivingReceptors.Add(new ReceptorConnection(gr)));
						}

						// Associate the receptor with the protocol it receives.
						protocolReceptorMap[rq.Protocol].Add(new ReceptorConnection(r));
					}
				});
		}

		/// <summary>
		/// Internal carrier creation.  This includes the "stopRecursion" flag to prevent wildcard receptors from receiving ad-infinitum their own emissions.
		/// </summary>
		protected ICarrier CreateCarrier(IReceptorInstance from, ISemanticTypeStruct protocol, string protocolPath, dynamic signal, bool stopRecursion, bool isSystemMessage = false, ICarrier parentCarrier = null, bool emitSubElements = true, bool isRoot = true)
		{
			Carrier carrier = null;

			if (from.GetEnabledEmittedProtocols().Any(p => p.Protocol == protocol.DeclTypeName) || isSystemMessage)
			{
				carrier = new Carrier(protocol, protocolPath, signal, parentCarrier);
				// ************ MOVED TO A: **************
				// The reason this was moved is so that we fire NewCarrier only for actual receptors with protocols enabled for that receptor.
				// TODO: However, this means that we no longer queue protocols for which we have no receptors.  Not sure if we actually want this particular feature.
				// NewCarrier.Fire(this, new NewCarrierEventArgs(from, carrier));

				// We pass along the stopRecursion flag to prevent wild-card carrier receptor from receiving their own emissions, which would result in a new carrier,
				// ad-infinitum.
				ProcessReceptors(from, carrier, stopRecursion, isRoot);
			}

			// Recurse into SE's of the protocol and emit carriers for those as well, if a receiver exists.
			if (!isSystemMessage && emitSubElements)
			{
				// The carrier might be null if there's no receiver for the parent carrier.  In this case, we need to create a dummy carrier so we have a parent.
				if (carrier == null)
				{
					carrier = new Carrier(protocol, protocolPath, signal, parentCarrier);
				}

				CreateCarriersForSemanticElements(from, protocol, protocol.DeclTypeName, signal, stopRecursion, carrier);
			}

			return carrier;
		}

		/// <summary>
		/// Recurse into SE's of the protocol and emit carriers for those as well, if a receiver exists.
		/// </summary>
		protected void CreateCarriersForSemanticElements(IReceptorInstance from, ISemanticTypeStruct protocol, string protocolPath, dynamic signal, bool stopRecursion, ICarrier parentCarrier)
		{
			protocol.SemanticElements.ForEach(se =>
				{
					dynamic subsignal = SemanticTypeSystem.Clone(signal, se); // Clone the contents of the signal's semantic element into the subsignal.

					// We may have a null child, in which case, don't drill any further into the structure!
					if (subsignal != null)
					{
						ISemanticTypeStruct semStruct = SemanticTypeSystem.GetSemanticTypeStruct(se.Name);
						// Will result in recursive calls for all sub-semantic types.
						CreateCarrierIfReceiver(from, semStruct, protocolPath + "." + semStruct.DeclTypeName, subsignal, parentCarrier);
					}
				});
		}

		/// <summary>
		/// Returns true if there is an enabled target from the specified receptor with the specified protocol.
		/// </summary>
		protected bool TargetReceptorExistsFor(IReceptor from, ISemanticTypeStruct protocol, bool isRoot)
		{
			bool ret = false;

			// Some bullet proofing that was revealed in unit testing.
			if (from != null)
			{
				List<IReceptorConnection> targets = new List<IReceptorConnection>();
				if (MasterReceptorConnectionList.TryGetValue(from, out targets))
				{
					// This annoying piece of code assumes that a receptor will have only one connection between "from" to "to".
					// In the case of the semantic database, a "from" receptor can have multiple connections with the same semantic database receptor.
					// In other words, the returned list consists of [n] identical instances, where [n] is the number of different protocols from "from" to the target receptor.
					// To fix this problem, we get only the distinct instances.
					targets = targets.Distinct().ToList();
					// We're only interested in enabled receptors, and we ignore ourselves.
					ret = targets.Any(r => r.Receptor != from && 
						r.Receptor.Instance.Enabled && 
						r.PermeabilityProtocol == protocol.DeclTypeName &&
						(!r.RootOnly || isRoot)); // &&			// root protocol or we don't care if it's not the root.
						// r.Receptor.Instance.GetEnabledReceiveProtocols().Select(rp => rp.Protocol).Contains(protocol.DeclTypeName));
				}

				if (!ret)
				{
					// check protocol map for receivers that are not the issuing receptor, ignoring ourselves.
					ret = protocolReceptorMap.Any(kvp => (kvp.Key == protocol.DeclTypeName) && 
						kvp.Value.Any(r => (r.Receptor != from) &&
							r.PermeabilityProtocol == protocol.DeclTypeName &&
							(!r.RootOnly || isRoot) &&			// root protocol or we don't care if it's not the root.
							(r.Receptor.Instance.Enabled))); // .ContainsKey(protocol.DeclTypeName);
				}
			}

			return ret;
		}

		// TODO: This code needs to be optimized.
		/// <summary>
		/// Returns the target receptors that will receive the carrier protocol, qualified by the receptor's optional condition on the signal.
		/// </summary>
		protected List<IReceptorConnection> GetTargetReceptorsFor(IReceptor from, ICarrier carrier, bool isRoot)
		{
			// Lastly, filter the list by qualified receptors that are not the source of the carrier.
			List<IReceptorConnection> newTargets = new List<IReceptorConnection>();

			// From can be null if we're changing the layout during the time when carriers are being processed,
			// specifically, when a receptor is moved into or out of a membrane or the receptor is taken off the surface.
			if (from != null)
			{
				List<IReceptorConnection> targets;
				ISemanticTypeStruct protocol = carrier.Protocol;

				// This annoying piece of code assumes that a receptor will have only one connection between "from" to "to".
				// In the case of the semantic database, a "from" receptor can have multiple connections with the same semantic database receptor.
				// In other words, the returned list consists of [n] identical instances, where [n] is the number of different protocols from "from" to the target receptor.
				if (!MasterReceptorConnectionList.TryGetValue(from, out targets))
				{
					// When the try fails, it sets targets to null.
					targets = new List<IReceptorConnection>();
				}

				// To fix the aformentioned problem, we get only the distinct instances.
				targets = targets.Distinct().ToList();

				// Only enabled receptors and receptors that are not the source of the carrier and our not ourselves.
				List<IReceptorConnection> filteredTargets = targets.Where(r => 
					(!r.RootOnly || isRoot) && 
					r.Receptor != from && 
					r.Receptor.Instance.Enabled && 
					r.PermeabilityProtocol == protocol.DeclTypeName).ToList(); // &&
					// r.Receptor.Instance.GetEnabledReceiveProtocols().Select(rq => rq.Protocol).Contains(protocol.DeclTypeName)).ToList();

				// Will have a count of 0 if the receptor is the system receptor, ie, carrier animations or other protocols.
				// TODO: This seems kludgy, is there a better way of working with this?
				// Also, if the emitting receptor doesn't declare its protocol, this count will be 0, leading to potentially strange results.
				// For example, comment out the persistence receptors "IDReturn" and run the feed reader example.  You'll see that TWO items
				// are returned as matching "RSSFeed" table name and for reasons unknown at the moment, protocolReceptorMap has two entries that qualify.
				if (filteredTargets.Count == 0)
				{
					// When the try fails, it sets targets to null.
					if (protocolReceptorMap.TryGetValue(protocol.DeclTypeName, out targets))
					{
						filteredTargets = targets.Where(r => r.Receptor.Instance.Enabled && (r.Receptor != from) && true).ToList();
						// Remove disabled receive protocols.
						filteredTargets = filteredTargets.Where(r => r.Receptor.Instance.GetReceiveProtocols().Exists(p => p.Protocol == protocol.DeclTypeName && p.Enabled)).ToList();
					}
				}

				filteredTargets.Where(r => r.Receptor != from && r.Receptor.Instance.Enabled).ForEach(t =>
					{
						// The PermeableProtocol may be a higher level protocol than the receiver receives, so we need to find the receivers that have sub-protocols of the current protocol enabled.
						List<ISemanticTypeStruct> protocols = protocol.FlattenedSemanticTypes();
						protocols.ForEach(se =>
							{
								string prot = se.DeclTypeName;
								// Get the list of receive actions and filters for the specific protocol.
								var receiveList = t.Receptor.Instance.GetEnabledReceiveProtocols().Where(rp => rp.Protocol == prot);
								receiveList.ForEach(r =>
									{
										// If qualified, add to the final target list.
										// REF: 03282015
										// TODO: It looks like we're already doing a qualifier check in BaseReceptor.  Probably we can remove THAT check!
										if (r.Qualifier(carrier.Signal))
										{
											newTargets.Add(t);
										}
									});
							});
					});

				// filteredTargets = filteredTargets.Where(t => t.Instance.GetReceiveProtocols().Any(rp => (rp.Protocol == protocol.DeclTypeName) && rp.Qualifier(carrier.Signal))).ToList();

				// Get the targets of the single receive protocol that matches the DeclTypeName and whose qualifier returns true.
				// filteredTargets = filteredTargets.Where(t => t.Instance.GetReceiveProtocols().Single(rp => rp.Protocol == protocol.DeclTypeName).Qualifier(carrier.Signal)).ToList();
			}
			else
			{
				// TODO: Should we log this error so we can further inspect the effects that it may have on processing carriers?
			}

			return newTargets;
		}

		/// <summary>
		/// Given a carrier, if there are receptors for the carrier's protocol, act upon the carrier immediately.
		/// If there are no receptors for the protocol, queue the carrier.
		/// </summary>
		protected void ProcessReceptors(IReceptorInstance from, Carrier carrier, bool stopRecursion, bool isRoot)
		{
			// Get the action that we are supposed to perform on the carrier.
			Action action = GetProcessAction(from, carrier, stopRecursion, isRoot);
			IReceptor receptor = ReceptorFromInstance(from);

			// Some bullet proofing that was revealed in unit testing.
			if (receptor != null)
			{
				List<IReceptorConnection> receptors = GetTargetReceptorsFor(ReceptorFromInstance(from), carrier, isRoot);

				// If we have any enabled receptor for this carrier (a mapping of carrier to receptor list exists and receptors actually exist in that map)...
				if (receptors.Count > 0)
				{
					// ...perform the action.
					action();
				}
				else
				{
					// ...othwerise, queue up the carrier for when there is a receptor for it.
					queuedCarriers.Add(new QueuedCarrierAction() { From = ReceptorFromInstance(from), Carrier = carrier, Action = action });
				}
			}
			else
			{
				// The "from" receptor is null, which is an invalid condition occurring during unit testing.
				// Regardless, queue the carrier.
				queuedCarriers.Add(new QueuedCarrierAction() { From = null, Carrier = carrier, Action = action });
			}
		}

		/// <summary>
		/// If a queued carrier has a receptor to receive the protocol, execute the action and remove it from the queue.
		/// </summary>
		public void ProcessQueuedCarriers(bool isRoot)
		{
			List<QueuedCarrierAction> removeActions = new List<QueuedCarrierAction>();

			// An action can result in carriers being created and queued if there's no receptor, so we walk through the existing 
			// collection with an indexer rather than a foreach.
			queuedCarriers.IndexerForEach(action =>
			{
				List<IReceptorConnection> receptors = GetTargetReceptorsFor(action.From, action.Carrier, isRoot);

				// If we have any enabled receptor for this carrier (a mapping of carrier to receptor list exists and receptors actually exist in that map)...
				if (receptors.Count > 0)
				{
					action.Action();
					// Collect actions that need to be removed.
					removeActions.Add(action);
				}
			});

			// Remove all processed actions.
			removeActions.ForEach(a => queuedCarriers.Remove(a));
		}

		/// <summary>
		/// Return an action representing what to do for a new carrier/protocol.
		/// </summary>
		protected Action GetProcessAction(IReceptorInstance from, Carrier carrier, bool stopRecursion, bool isRoot)
		{
			// Construct an action...
			Action action = new Action(() =>
				{
					// Get the receptors receiving the protocol.
					List<IReceptorConnection> receptors = GetTargetReceptorsFor(ReceptorFromInstance(from), carrier, isRoot);

					// For each receptor that is enabled...
					receptors.ForEach(receptor =>
					{
						// The action is "ProcessCarrier".
						// TODO: *** Pass in the carrier, not the carrier's fields!!! ***
						// ************* A: MOVED HERE ************
						NewCarrier.Fire(this, new NewCarrierEventArgs(from, carrier));
						Action process = new Action(() => receptor.Receptor.Instance.ProcessCarrier(carrier));

						// TODO: This flag is tied in with the visualizer, we should extricate this flag and logic.
						if (receptor.Receptor.Instance.IsHidden)
						{
							// Don't visualize carriers to hidden receptors.
							process();
						}
						else if (!stopRecursion)
						{
							// TODO: This should be handled externally somehow.
							// Defer the action, such that the visualizer can invoke it when it determines the carrier rendering to the receiving receptor has completed.
							ISemanticTypeStruct protocol = SemanticTypeSystem.GetSemanticTypeStruct("CarrierAnimation");
							dynamic signal = SemanticTypeSystem.Create("CarrierAnimation");
							signal.Process = process;
							signal.From = from;
							signal.To = receptor.Receptor.Instance;
							signal.Carrier = carrier;
							// Simulate coming from the system, as it IS a system message.
							// Also note that the "stop recursion" flag is set to true on a receptor defining "receives everything" ("*").
							CreateCarrier(from, protocol, protocol.DeclTypeName, signal, receptor.Receptor.Instance.GetEnabledReceiveProtocols().Select(rp=>rp.Protocol).Contains("*"), true);
						}
					});
				});

			return action;
		}

		/// <summary>
		/// TODO: Remove this to outside this class.
		/// </summary>
		/// <param name="r"></param>
		protected void AnnounceReceptor(IReceptor r)
		{
			Say("Receptor " + r.Name + " online.");
		}

		// TODO: Duplicate code.
		// TODO: We probably could use some global carrier creation library.
		protected void Say(string msg)
		{
			// TODO: I've turned this off because every membrane's receptor system is now talking!!!!

			//ISemanticTypeStruct protocol = SemanticTypeSystem.GetSemanticTypeStruct("TextToSpeech");
			//dynamic signal = SemanticTypeSystem.Create("TextToSpeech");
			//signal.Text = msg;
			//CreateCarrier(null, protocol, signal);
		}

		protected void WhenEnabledStateChanged(object sender, ReceptorEnabledEventArgs e)
		{
			// Any queued carriers are now checked to determine whether receptors are now enabled to process their protocols.
			ProcessQueuedCarriers(true);
		}

		protected IReceptor ReceptorFromInstance(IReceptorInstance inst)
		{
			return receptors.SingleOrDefault(r => r.Instance == inst);
		}
	}
}
