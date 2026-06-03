using QuantumHangar;
using Sandbox.Game.Entities;
using System.Collections.Generic;
using System.Linq;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game;
using VRageMath;

namespace QuantumHangar.Utils
{
    public class PreviewBoxTimer
    {
        //Following list keeps track of all displays to everyone on the server
        public static List<PreviewBoxTimer> list = new List<PreviewBoxTimer>();


        public int timeDisplayed = 0;
        private const int DefaultDisplayFor = 60; //display interferred boxes for 60 seconds
        private readonly int _displayFor;

        public DrawDebug drawobjectMessage;
        private ulong _target;

        public PreviewBoxTimer(ulong target, int displayFor = DefaultDisplayFor)
        {
            drawobjectMessage = new DrawDebug(target.ToString());
            _target = target;
            _displayFor = displayFor;
        }

        public void display()
        {
            PreviewBoxTimer exsisting = list.FirstOrDefault(x => x == this);
            if (exsisting != null)
            {
                exsisting.remove();
                list.Remove(exsisting);
            }



            list.Add(this);
            ModCommunication.SendMessageTo(drawobjectMessage, _target);

            //clear
            drawobjectMessage.drawObjects.Clear();
        }


        public static void Update()
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].timeDisplayed >= list[i]._displayFor)
                {
                    list[i].remove();
                    list.RemoveAt(i);
                    continue;
                }

                list[i].timeDisplayed++;
            }
        }

        public static void removeAll(ulong id)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i]._target != id)
                    continue;


                list[i].remove();
                list.RemoveAt(i);
            }
        }

        public static void DisplayGridSelection(ulong target, GridStamp stamp,
            int displayFor = DefaultDisplayFor)
        {
            removeAll(target);

            var timer = new PreviewBoxTimer(target, displayFor);
            var color = new Color(255, 255, 96, 255);
            timer.drawobjectMessage.addOBB(stamp.Box, stamp.MatrixTranslation, stamp.BoundingBox.Orientation.Forward,
                stamp.BoundingBox.Orientation.Up, color, MySimpleObjectRasterizer.Wireframe, 1.2f, 0.01f);

            var drawBox = timer.drawobjectMessage.drawObjects.LastOrDefault();
            if (drawBox != null)
                drawBox.wireDivideRatio = 1;

            timer.display();
        }

        private void remove()
        {
            drawobjectMessage.remove = true;
            ModCommunication.SendMessageTo(drawobjectMessage, _target);
        }

        public override bool Equals(object obj)
        {
            if (obj is PreviewBoxTimer timer)
                return timer._target == _target;

            return false;
        }

        public override int GetHashCode()
        {
            return _target.GetHashCode();
        }
    }
}
