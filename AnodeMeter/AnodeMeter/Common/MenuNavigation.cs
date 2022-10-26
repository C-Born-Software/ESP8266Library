using System;
//using Microsoft.SPOT;
using System.Collections;

namespace AnodeMeter.Common
{
    public class MenuNavigation
    {
        private ArrayList theMenuLevels = new ArrayList();
        private int CurrentMenuLevel = 0;
        private int WindowWidth;

        //private ArrayList AHSubtypes = new ArrayList();
        /// <summary>
        /// Adhoc types - first entry AH. Read from SmelterConfiguration.xml
        /// </summary>
        public string[] AHSubtypes;
        private int AHSubtypeIndex = 0;


        public MenuNavigation(string[] theLevels, int WindowWidth)
        {
            this.WindowWidth = WindowWidth;
            CurrentMenuLevel = 0;

            foreach (string s in theLevels)
            {
                theMenuLevels.Add(new MenuLevel(s));
            }
        }

        public void SetChoices(string MenuLevel, ArrayList Choices)
        {
            foreach (MenuLevel ml in theMenuLevels)
            {
                if (ml.MenuLevelName == MenuLevel)
                {
                    ml.Menu = new ChoiceList();
                    ml.Menu.Capacity = Choices.Count;
                    foreach (string choice in Choices)
                    {
                        ml.Menu.Add(choice);
                    }
                }
            }
        }

        //==== AdHoc Subtypes 
        public string SubChoice(string choice)
        {
            if (choice == "AH")
                return AHSubtypes[AHSubtypeIndex];
            return choice;
        }

        public void MoveAHSubtype(int where)
        {
            if (where == 0)
                AHSubtypeIndex = 0;
            else
            {
                AHSubtypeIndex += where;
                if (AHSubtypeIndex < 0)
                    AHSubtypeIndex = AHSubtypes.Length - 1;
                else if (AHSubtypeIndex >= AHSubtypes.Length)
                    AHSubtypeIndex = 0;
            }
        }
        //====

        internal string Display()
        {
            string Output = "";
            //int charPointer = 0;
            bool WindowNotFilled = true;


            ChoiceList cl = ((MenuLevel)theMenuLevels[CurrentMenuLevel]).Menu;
            if (cl != null)
            {
                int clCount = 0;
                string choice = "(" + SubChoice(cl.CurrentChoice()) + ") ";
                int saveindex = cl.CurrentChoiceIndex;
                Output += choice;
                clCount = cl.Count;

                while (WindowNotFilled)
                {
                    choice = cl.NextChoice() + " ";

                    if (--clCount == 0)  // If we have added all choices without filling window, exit
                        break;
                    char[] charChoices = choice.ToCharArray();
                    if (charChoices.Length > WindowWidth - Output.Length)
                        WindowNotFilled = false;
                    else
                    {
                        foreach (char c in charChoices)
                        {
                            Output += c;
                        }
                    }
                }
                cl.CurrentChoiceIndex = saveindex;
                while (Output.Length < WindowWidth)
                    Output += ' ';
            }
            return Output.ToString();
        }

        internal int CurrentMenu()
        {
            return CurrentMenuLevel;
        }

        internal void MoveNextMenu()
        {
            CurrentMenuLevel = (CurrentMenuLevel + 1) % theMenuLevels.Count;
            //++CurrentMenuLevel;
            //if (CurrentMenuLevel > theMenuLevels.Count)
            //    CurrentMenuLevel = theMenuLevels.Count;
        }

        internal void MoveToTopLevelMenu()
        {
            CurrentMenuLevel = 0;
        }

        internal void MoveToBottomLevelMenu()
        {
            CurrentMenuLevel = theMenuLevels.Count - 1;
        }

        internal void MovePreviousMenu()
        {
            CurrentMenuLevel = (CurrentMenuLevel + theMenuLevels.Count - 1) % theMenuLevels.Count;
            //--CurrentMenuLevel;
            //if (CurrentMenuLevel < 0)
            //    CurrentMenuLevel = 0;
        }

        internal void MoveLeft()
        {
            ChoiceList cl = ((MenuLevel)theMenuLevels[CurrentMenuLevel]).Menu;
            if (cl != null)
                cl.PreviousChoice();
        }

        internal void MoveRight()
        {
            ChoiceList cl = ((MenuLevel)theMenuLevels[CurrentMenuLevel]).Menu;
            if (cl != null)
                cl.NextChoice();
        }

        internal string GetCurrentChoice()
        {
            string currentChoice = "";
            try
            {
                ChoiceList cl = ((MenuLevel)theMenuLevels[CurrentMenuLevel]).Menu;
                if (cl != null)
                    currentChoice = cl.CurrentChoice();
            }
            catch (Exception ex)
            {
                string mlc = theMenuLevels == null ? " null" : theMenuLevels.Count.ToString();
                Logging.IssueEvent(Logging.ErrSeverity.Warning, "MenuNavigation::GetCurrentChoice", "Exception thrown: \"CurrentMenuLevel\"= " + CurrentMenuLevel.ToString() + ", \"theMenuLevels.Count\"= " + mlc + ". Reason: " + ex.Message + "; StackTrace: " + ex.StackTrace, "Software err!");

                // Default to first option if exception thrown
                if (theMenuLevels != null && theMenuLevels.Count > 0)
                    currentChoice = (((MenuLevel)theMenuLevels[0]).Menu).CurrentChoice();
            }
            return currentChoice;
        }

        internal void AppendChoice(string MenuLevel, string Choice)
        {
            foreach (MenuLevel ml in theMenuLevels)
            {
                if (ml.MenuLevelName == MenuLevel)
                {
                    ml.Menu.Add(Choice);
                }
            }
        }

        internal void SetChoiceZero()
        {
            if (theMenuLevels.Count > CurrentMenuLevel)
            {
                ChoiceList cl = ((MenuLevel)theMenuLevels[CurrentMenuLevel]).Menu;
                if (cl != null)
                    cl.SetChoiceIndex();
            }
        }
    }


    public class ChoiceList : ArrayList
    {
        public int CurrentChoiceIndex { get; set; }

        public ChoiceList()
        {
            CurrentChoiceIndex = 0;
        }

        public string CurrentChoice()
        {
            return ToString();
        }
        public string NextChoice()
        {
            ++CurrentChoiceIndex;
            if (CurrentChoiceIndex > base.Count - 1)
                CurrentChoiceIndex = 0;
            return ToString();
        }
        public string PreviousChoice()
        {
            --CurrentChoiceIndex;
            if (CurrentChoiceIndex < 0)
                CurrentChoiceIndex = base.Count - 1;
            return ToString();
        }

        public void SetChoiceIndex()
        {
            CurrentChoiceIndex = 0;
        }

        public override string ToString()
        {
            return base[CurrentChoiceIndex].ToString();
        }
    }

    public class MenuLevel
    {
        public string MenuLevelName;
        public ChoiceList Menu;
        public MenuLevel(string MenuLevelName)
        {
            this.MenuLevelName = MenuLevelName;
        }
    }

}
