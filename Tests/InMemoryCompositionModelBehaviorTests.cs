namespace StockSharp.Tests;

using StockSharp.Diagram;
using StockSharp.Diagram.Elements;

/// <summary>
/// The behaviour is the store a diagram host reads and writes its graph through, and its notifications are
/// the only way that host learns the graph moved on. What it owes is therefore not a particular constant
/// on an event but an answer that is current by the time it is given: which links are still there, which
/// node has to be read again, and whether the diagram changed at all.
/// </summary>
[TestClass]
public class InMemoryCompositionModelBehaviorTests : BaseTestClass
{
	/// <summary>
	/// A behaviour with the model a host actually reads it through, and the one call a test needs to put
	/// an element into it.
	/// </summary>
	private sealed class Graph
	{
		public Graph()
		{
			Behavior = new();
			Model = new(Behavior);
		}

		public InMemoryCompositionModelBehavior Behavior { get; }
		public CompositionModel<InMemoryCompositionModelNode, InMemoryCompositionModelLink> Model { get; }

		public InMemoryCompositionModelNode Add(DiagramElement element)
		{
			var node = new InMemoryCompositionModelNode { Element = element };
			Model.AddNode(node);
			return node;
		}
	}

	private static string SocketId(StaticSocketIds id) => id.ToString();

	/// <summary>
	/// A link names the socket it lands on, and an element may give that socket up and take it back - a
	/// limit order has a price input, a market order has none. When the socket returns the link must be
	/// honoured: the model has to expose its far end again, and the host has to be told which node to read
	/// again, or the diagram is drawn with a link nobody can follow.
	/// </summary>
	[TestMethod]
	public void SocketAdded_InvalidatesTheRelationshipsTheModelExposes()
	{
		var graph = new Graph();

		var source = new OrderRegisterDiagramElement();
		var target = new OrderRegisterDiagramElement { IsMarket = true };

		var sourceNode = graph.Add(source);
		var targetNode = graph.Add(target);

		var orderId = SocketId(StaticSocketIds.Order);
		var priceId = SocketId(StaticSocketIds.Price);

		target.InputSockets.FindById(priceId).AssertNull("a market order element has no price input for a link to land on");

		graph.Model.AddLink(sourceNode, orderId, targetNode, priceId);

		InMemoryCompositionModelNode invalidated = null;

		graph.Behavior.BehaviorChanged += t =>
		{
			if (t.change == ModelChange.InvalidateRelationships)
				invalidated = (InMemoryCompositionModelNode)t.data;
		};

		target.IsMarket = false;

		var price = target.InputSockets.FindById(priceId);

		price.AssertNotNull("a limit order element must have its price input back");
		invalidated.AssertSame(targetNode, "the node that gained the socket is the one whose relationships have to be read again");
		graph.Behavior.Links.Count().AssertEqual(1, "a link whose socket came back leads somewhere again and must be kept");

		((ICompositionModel)graph.Model).GetConnectedSocketsFor(target, price).Single()
			.AssertSame(source.OutputSockets.FindById(orderId), "the model must expose the far end of the link once the socket it lands on exists");
	}

	/// <summary>
	/// The mirror promise. A socket an element gives up leaves the link that landed on it pointing nowhere,
	/// so the model must stop exposing that link - and must have stopped before the host is told the node
	/// changed, or the host reads the node back and is handed the relationship that has just died.
	/// </summary>
	[TestMethod]
	public void SocketRemoved_DropsTheLinkThatLandedOnIt_BeforeTheNodeIsAnnouncedAsChanged()
	{
		var graph = new Graph();

		// Both are limit order elements, so the target has a price input for the link to land on.
		var source = new OrderRegisterDiagramElement();
		var target = new OrderRegisterDiagramElement();

		var sourceNode = graph.Add(source);
		var targetNode = graph.Add(target);

		var orderId = SocketId(StaticSocketIds.Order);
		var priceId = SocketId(StaticSocketIds.Price);

		graph.Model.AddLink(sourceNode, orderId, targetNode, priceId);

		graph.Behavior.Links.Count().AssertEqual(1, "the link must be in the model before the socket goes");

		InMemoryCompositionModelNode invalidated = null;
		var linksWhenAnnounced = -1;

		graph.Behavior.BehaviorChanged += t =>
		{
			if (t.change != ModelChange.InvalidateRelationships)
				return;

			invalidated = (InMemoryCompositionModelNode)t.data;
			linksWhenAnnounced = graph.Behavior.GetLinksForNode(invalidated).Count();
		};

		target.IsMarket = true;

		target.InputSockets.FindById(priceId).AssertNull("a market order element has no price input");
		invalidated.AssertSame(targetNode, "the node that lost the socket is the one whose relationships have to be read again");
		graph.Behavior.Links.Count().AssertEqual(0, "a link that lands nowhere must stop being part of the graph");
		linksWhenAnnounced.AssertEqual(0, "the dead link must already be gone when the host is told to read the node again");
	}

	/// <summary>
	/// A host keeps a diagram it can save and redraw, and the model is the only place it learns the diagram
	/// moved on. An edit committed on an element is such a move: it must reach the host, and it must name
	/// the node and the operation so the right part can be redrawn or undone. Turning editing off changes
	/// nothing anyone could save, so it must not be announced as a change of the diagram.
	/// </summary>
	[TestMethod]
	public void CommittedElementEdit_IsAnnouncedAsAChangeOfTheDiagram_AndEditabilityIsNot()
	{
		var graph = new Graph();
		var node = graph.Add(new OrderRegisterDiagramElement());

		var changes = 0;
		graph.Model.ModelChanged += () => changes++;

		object edited = null;
		string operation = null;

		graph.Behavior.BehaviorChanged += t =>
		{
			if (t.change != ModelChange.Property)
				return;

			edited = t.data;
			operation = t.propName;
		};

		graph.Behavior.RaiseCommited("op", node, null);

		changes.AssertEqual(1, "an edit committed on an element is a change of the diagram and must reach the host");
		edited.AssertSame(node, "the announcement must name the node that was edited");
		operation.AssertEqual("op", "the announcement must name the operation that was committed");

		graph.Behavior.Modifiable = false;

		changes.AssertEqual(1, "turning editing off changes nothing that could be saved, so it is not a change of the diagram");
	}
}
