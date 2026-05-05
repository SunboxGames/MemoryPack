#pragma warning disable CS8602

using MemoryPack.Tests.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace MemoryPack.Tests;

public class CircularReferenceTest
{
    [Fact]
    public void MicrosoftExample()
    {
        // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/preserve-references?pivots=dotnet-7-0

        Employee tyler = new()
        {
            Name = "Tyler Stein"
        };

        Employee adrian = new()
        {
            Name = "Adrian King"
        };

        tyler.DirectReports = new List<Employee> { adrian };
        adrian.Manager = tyler;

        var bin = MemoryPackSerializer.Serialize(tyler);
        Employee? tylerDeserialized = MemoryPackSerializer.Deserialize<Employee>(bin);

        tylerDeserialized?.DirectReports?[0].Manager.Should().BeSameAs(tylerDeserialized);
    }

    [Fact]
    public void NodeTest()
    {
        var parent = new Node();
        var a1 = new Node();
        var a2 = new Node();
        var a3 = new Node();
        a1.Parent = parent;
        a2.Parent = parent;
        a3.Parent = parent;
        parent.Children = new[] { a1, a2, a3 };

        var bin = MemoryPackSerializer.Serialize(parent);
        var value2 = MemoryPackSerializer.Deserialize<Node>(bin);

        foreach (var item in value2!.Children)
        {
            item.Parent.Should().BeSameAs(value2);
        }
    }

    [Fact]
    public void PureNodeTest()
    {
        var node = new PureNode() { Id = 10, Id2 = 1000 };

        var bin = MemoryPackSerializer.Serialize(node);
        var value2 = MemoryPackSerializer.Deserialize<PureNode>(bin);

        value2.Id.Should().Be(10);
        value2.Id2.Should().Be(1000);
    }

    [Fact]
    public void InHolder()
    {
        var holder = new CircularHolder();
        holder.List = new List<Node>();
        holder.ListPure = new List<PureNode>();

        {
            var parent = new Node();
            var a1 = new Node();
            var a2 = new Node();
            var a3 = new Node();
            a1.Parent = parent;
            a2.Parent = parent;
            a3.Parent = parent;
            parent.Children = new[] { a1, a2, a3 };

            var parent2 = new Node();
            parent2.Children = new[] { parent, a2 };

            holder.List.AddRange(new[] { parent, parent, parent2, parent, parent2 });
        }
        {
            var pure1 = new PureNode() { Id = 10, Id2 = 1000 };
            var pure2 = new PureNode() { Id = 100, Id2 = 100000 };

            holder.ListPure.Add(pure1);
            holder.ListPure.Add(pure1);
            holder.ListPure.Add(pure2);
            holder.ListPure.Add(pure2);
            holder.ListPure.Add(pure1);
        }


        var bin = MemoryPackSerializer.Serialize(holder);
        var value2 = MemoryPackSerializer.Deserialize<CircularHolder>(bin);

        {
            var parent = value2.List[0];
            var parent2 = value2.List[2];
            var a1 = parent.Children[0];
            var a2 = parent.Children[1];
            var a3 = parent.Children[2];

            parent.Should().NotBeSameAs(parent2);
            parent2.Children[0].Should().BeSameAs(parent);
            parent2.Children[1].Should().BeSameAs(a2);
        }
        {
            var pure1 = value2.ListPure[0];
            var pure2 = value2.ListPure[2];

            pure1.Should().NotBeSameAs(pure2);
            pure1.Should().BeSameAs(value2.ListPure[1]);
            pure1.Should().BeSameAs(value2.ListPure[4]);
            pure2.Should().BeSameAs(value2.ListPure[3]);
        }
    }

    [Fact]
    public void Sequential()
    {
        SequentialCircularReference tyler = new()
        {
            Name = "Tyler Stein"
        };

        SequentialCircularReference adrian = new()
        {
            Name = "Adrian King"
        };

        tyler.DirectReports = new List<SequentialCircularReference> { adrian };
        adrian.Manager = tyler;

        var bin = MemoryPackSerializer.Serialize(tyler);
        SequentialCircularReference? tylerDeserialized = MemoryPackSerializer.Deserialize<SequentialCircularReference>(bin);

        tylerDeserialized?.DirectReports?[0].Manager.Should().BeSameAs(tylerDeserialized);
    }

    // Builds a chain of `length` nodes and verifies round-trip without StackOverflow.
    static LinkedNode BuildLinkedList(int length)
    {
        LinkedNode head = new() { Value = 0 };
        var current = head;
        for (int i = 1; i < length; i++)
        {
            var next = new LinkedNode { Value = i };
            current.Next = next;
            current = next;
        }
        return head;
    }

    static int CountAndVerifyChain(LinkedNode? head)
    {
        int count = 0;
        var current = head;
        while (current != null)
        {
            current.Value.Should().Be(count);
            count++;
            current = current.Next;
        }
        return count;
    }

    [Fact]
    public void DeepLinkedList_PastDefaultThreshold()
    {
        // 5,000 nodes — well past the default MaxDepth of 512, exercises defer + drain.
        const int length = 5_000;
        var head = BuildLinkedList(length);

        var bin = MemoryPackSerializer.Serialize(head);
        var deserialized = MemoryPackSerializer.Deserialize<LinkedNode>(bin);

        CountAndVerifyChain(deserialized).Should().Be(length);
    }

    [Fact]
    public void DeepLinkedList_VeryLong()
    {
        // 100,000 nodes — would StackOverflow without defer.
        const int length = 100_000;
        var head = BuildLinkedList(length);

        var bin = MemoryPackSerializer.Serialize(head);
        var deserialized = MemoryPackSerializer.Deserialize<LinkedNode>(bin);

        CountAndVerifyChain(deserialized).Should().Be(length);
    }

    [Fact]
    public void DeepLinkedList_TightThreshold()
    {
        // Force defer to fire frequently with MaxDepth=4. Stresses the drain machinery.
        const int length = 1_000;
        var head = BuildLinkedList(length);

        var options = MemoryPackSerializerOptions.Default with { MaxDepth = 4 };
        var bin = MemoryPackSerializer.Serialize(head, options);
        var deserialized = MemoryPackSerializer.Deserialize<LinkedNode>(bin, options);

        CountAndVerifyChain(deserialized).Should().Be(length);
    }

    [Fact]
    public void DeepBinaryTree()
    {
        // Balanced binary tree with depth 14 = 16,383 nodes. Exercises defer with wide fan-out.
        TreeNode Build(int depth, int seed)
        {
            var node = new TreeNode { Value = seed };
            if (depth <= 0) return node;
            node.Left = Build(depth - 1, seed * 2 + 1);
            node.Right = Build(depth - 1, seed * 2 + 2);
            return node;
        }
        bool Equal(TreeNode? a, TreeNode? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.Value == b.Value && Equal(a.Left, b.Left) && Equal(a.Right, b.Right);
        }

        var root = Build(13, 0);
        var bin = MemoryPackSerializer.Serialize(root);
        var deserialized = MemoryPackSerializer.Deserialize<TreeNode>(bin);

        Equal(root, deserialized).Should().BeTrue();
    }

    [Fact]
    public void CyclePastThreshold()
    {
        // Build a chain of 1000, then close the cycle: last.Next = head. Must round-trip
        // with cycle preserved despite passing MaxDepth=512 mid-chain.
        const int length = 1_000;
        var head = BuildLinkedList(length);
        var tail = head;
        while (tail.Next != null) tail = tail.Next;
        tail.Next = head;

        var bin = MemoryPackSerializer.Serialize(head);
        var deserialized = MemoryPackSerializer.Deserialize<LinkedNode>(bin)!;

        // Walk `length` nodes, then verify we're back at head.
        var current = deserialized;
        for (int i = 0; i < length - 1; i++) current = current.Next!;
        current.Next.Should().BeSameAs(deserialized);
    }

    [Fact]
    public void PlainObject_DeepChain_Throws()
    {
        // Plain GenerateType.Object types don't participate in defer — they should hit the
        // hard DepthLimit guard and throw cleanly rather than StackOverflow.
        const int length = 2_000;
        PlainLinkedNode head = new() { Value = 0 };
        var current = head;
        for (int i = 1; i < length; i++)
        {
            var next = new PlainLinkedNode { Value = i };
            current.Next = next;
            current = next;
        }

        Action act = () => MemoryPackSerializer.Serialize(head);
        act.Should().Throw<MemoryPackSerializationException>();
    }

}
