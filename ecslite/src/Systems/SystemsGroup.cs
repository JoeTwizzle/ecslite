using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EcsLite.Systems
{
    internal class StringTree
    {
        public StringTree? Parent;
        public readonly string PartialIdentifier;
        public readonly string FullIdentifier;
        public List<StringTree> Children;

        public StringTree(StringTree? parent, string partialIdentifier)
        {
            PartialIdentifier = partialIdentifier;
            if (parent is null)
            {
                FullIdentifier = partialIdentifier;
            }
            else
            {
                FullIdentifier = $"{parent.FullIdentifier}.{partialIdentifier}";
            }
            Children = new List<StringTree>();
        }

        public void AddChild(string name)
        {
            Children.Add(new StringTree(this, name));
        }
    }
    internal class StringTreeRoot
    {
        public Stack<int> CurrentIndices;
        public List<StringTree> Children;
        public StringTreeRoot()
        {
            CurrentIndices = new Stack<int>();
            Children = new List<StringTree>();
        }
        public void BeginGroup(string name)
        {
            StringTree? next = null;
            List<StringTree> children = Children;
            foreach (var index in CurrentIndices)
            {
                next = children[index];
                children = next.Children;
            }
            if (next is null)
            {
                Children.Add(new StringTree(null, name));
            }
            else
            {
                next.AddChild(name);
            }
            CurrentIndices.Push(Children.Count - 1);
        }

        public void EndGroup()
        {
            CurrentIndices.Pop();
        }
    }

    public sealed partial class EcsSystems
    {
        private float _currentDelayTime;
        private bool _currentGroupState;
        private string? _currentGroupName;
        Queue<(string, bool)> _currentGroupNameQueue;
        private StringTreeRoot _root;
        Dictionary<string, List<EcsTickedSystem>> _tickedSystems;
        /// <summary>
        /// Sets how long systems should wait between execution
        /// </summary>
        /// <param name="delay">delay in Milliseconds</param>
        public void SetTickDelay(float delay)
        {
            _currentDelayTime = delay / 1000.0f;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Name"></param>
        public void BeginGroup(string name, bool defaultState = true)
        {
            _root.BeginGroup(name);
        }

        public void EndGroup()
        {
            _root.EndGroup();
            _currentGroupState = true;
        }

        public void EnableGroupNextFrame(string groupName)
        {
            _currentGroupNameQueue.Enqueue((groupName, false));
        }

        public void SetGroupNextFrame(string groupName, bool state)
        {
            _currentGroupNameQueue.Enqueue((groupName, state));
        }

        public void DisableGroupNextFrame(string groupName)
        {
            _currentGroupNameQueue.Enqueue((groupName, true));
        }

        void ProcessGroupStates()
        {
            foreach (var pair in _currentGroupNameQueue)
            {
                var systems = _tickedSystems[pair.Item1];
                foreach (var system in systems)
                {
                    system.Enabled = pair.Item2;
                }
            }
            _currentGroupNameQueue.Clear();
        }
    }
}
