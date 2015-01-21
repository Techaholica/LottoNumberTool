using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace LottoNumberTool
{
    class LottoNumberTool
    {
        //random numbers where the numbers chosen more are weighted more
        public List<int> posChoices {get;set;}

        //random numbers chosen where the numbers chosen least are weighted more
        public List<int> negChoices {get;set;}

        //random numbers chosen where all numbers are weighted equally
        public List<int> controlChoices { get; set; }

        //holds the lotto numbers and their frequency drawn
        public List<LottoNumber> numbers {get;set;}

        //the connection to the database
        public MySqlConnection myConnection {get;set;}

        //holds todays winning numbers
        public List<string> winningNumbers {get;set;}

        public LottoNumberTool()
        {
            //create database connection
            string username = "username";
            string password = "password";
            string server = "server";
            string database = "database";

            //create the database connection.
            myConnection = new MySqlConnection("server=" + server + ";user id=" + username + ";password=" + password + ";database=" + database + ";pooling=false");
            myConnection.Open();
        }

        //a simple class to hold the lotto number and its occurences
        public class LottoNumber : IComparable<LottoNumber>
        {
            public int number {get;set;}
            public int occurence {get;set;}

            public LottoNumber(int n, int o)
            {
                number = n;
                occurence = o;
            }

            //when we compare to lotto numbers to sort, we look at the occurences
            public int CompareTo(LottoNumber other)
            {
                return occurence.CompareTo(other.occurence);
            }
        }

        //take a list of weighted numbers and returns 6 lotto numbers
        public static List<int> ChooseNumbers(List<int> weightedNumbers)
        {
            Random rand = new Random();
            List<int> choices = new List<int>();
            while(choices.Count < 6)
            {
                int selection = rand.Next(1, weightedNumbers.Count);
                if (!choices.Contains(weightedNumbers[selection]))
                {
                    choices.Add(weightedNumbers[selection]);
                }
            }
            choices.Sort();
            return choices;
        }

        //given a list of LottoNumbers, take the occurences and write out the numbers that many times (creates a weighted list)
        public static List<int> CreateWeightedNumbers(List<LottoNumber> numbers)
        {
            List<int> weightedNumbers = new List<int>();
            foreach (LottoNumber num in numbers)
            {
                for (int i = 0; i < num.occurence; i++)
                {
                    weightedNumbers.Add(num.number);
                }
            }
            return weightedNumbers;
        }

        public static string ChoicesToString(List<int> choices)
        {
            string c = "";
            foreach (int choice in choices)
            {
                c += "'" + choice.ToString() + "',";
            }
            c = c.Substring(0, c.Length - 1); //remove the last ,
            return c; 
        }

        public static string ChoicesToString(List<string> choices)
        {
            string c = "";
            foreach (string choice in choices)
            {
                c += "'" + choice + "',";
            }
            c = c.Substring(0, c.Length - 1); //remove the last ,
            return c;
        }

        public static int GetMatched(List<string> winning, List<int> numbers)
        {
            int matched = 0;
            foreach (int num in numbers)
            {
                if (winning.Contains(num.ToString()))
                {
                    matched++;
                }
            }
            return matched;
        }

        public void GetNumberFrequences()
        {
            string url = "http://www.txlottery.org/export/sites/lottery/Games/Lotto_Texas/Number_Frequency.html";
            numbers = new List<LottoNumber>(); //so we get numbers 1-54 and there is also a 0 there we can ignore
            int number = 1; //we will start counting at 1
            int biggest = 1; //use this to track what the largest number frequency is. Will use later in the inverse weighted odds section
            HtmlWeb hw = new HtmlWeb();
            HtmlAgilityPack.HtmlDocument doc = hw.Load(url);
            //HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes("//td[@class='freqDistLot']");
            //HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes("//table[@class='rt-responsive-table']");
            HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes("//table//td");

            //loop through all nodes and create a list of LottoNumbers
            //also write these numbers to the database to use in the charts
            MySqlCommand myCommand;
            foreach (HtmlNode node in nodes)
            {
                if (number < 55)
                {
                    if (!node.InnerHtml.Contains("<span"))
                    {
                        int nodeNum = Convert.ToInt32(node.InnerText);
                        if (nodeNum > biggest) biggest = nodeNum; //check if we have a new biggest value
                        numbers.Add(new LottoNumber(number, nodeNum));

                        //write this number to the table
                        myCommand = new MySqlCommand("UPDATE FrequencyNumbers SET Frequency=" + nodeNum + " WHERE Number=" + number, myConnection);
                        myCommand.ExecuteNonQuery();

                        number++;
                    }
                }
            }
        }

        public void PickRandomNumbers()
        {
            //pick positive weighted numbers where numbers chosen more are weighted more
            List<int> weightedNumbers = CreateWeightedNumbers(numbers);
            posChoices = ChooseNumbers(weightedNumbers);

            //Pick a the negative weighted numbers where numbers chosen least are weighted more.
            //Pull the inverse of the above to get 6 random numbers where the least chosen is weighted greater
            //take the above list and sort it in ascending and descending. Take the numbers from the ascending list
            //and join them with the occurences of the descending list to make a new list with inverse values
            numbers.Sort((a, b) => a.CompareTo(b)); //sort the original list in ascending order
            List<LottoNumber> descList = (numbers.OrderByDescending(o => o)).ToList(); //make a different list in descending order
            //create a new list of where the lowest occurence is weighted the highest
            List<LottoNumber> descNumbers = new List<LottoNumber>();
            for (int i = 0; i < numbers.Count; i++)
            {
                descNumbers.Add(new LottoNumber(numbers[i].number, descList[i].occurence));
            }

            weightedNumbers.Clear(); //clear the old weighted numbers list
            weightedNumbers = CreateWeightedNumbers(descNumbers);
            negChoices = ChooseNumbers(weightedNumbers);

            //choose the control numbers. Just choose 6 random numbers from 1-54
            Random rand = new Random();
            controlChoices = new List<int>();
            while (controlChoices.Count < 6)
            {
                int selection = rand.Next(1, 54);
                if (!controlChoices.Contains(selection))
                {
                    controlChoices.Add(selection);
                }
            }
            controlChoices.Sort();

            //now that we have our numbers, we need to write them to the database
            string pChoicesString = ChoicesToString(posChoices);
            string nChoicesString = ChoicesToString(negChoices);
            string cChoicesString = ChoicesToString(controlChoices);
            string date = DateTime.Today.ToString("MM/dd/yy"); //gets date in mm/dd/yyyy format
            MySqlCommand myCommand;

            //Check if chosen numbers match winning numbers and post matches
            int pMatched = GetMatched(winningNumbers, posChoices);
            int nMatched = GetMatched(winningNumbers, negChoices);
            int cMatched = GetMatched(winningNumbers, controlChoices);

            //first insert the positive weighted choices into the table
            myCommand = new MySqlCommand("INSERT INTO ChosenNumbers (n1, n2, n3, n4, n5, n6, date, type, matching) Values (" + pChoicesString + ",'" + date + "', 'positive', '" + pMatched + "')", myConnection);
            myCommand.ExecuteNonQuery();
            //then the negative weighted choices
            myCommand = new MySqlCommand("INSERT INTO ChosenNumbers (n1, n2, n3, n4, n5, n6, date, type, matching) Values (" + nChoicesString + ",'" + date + "','negative', '" + nMatched + "')", myConnection);
            myCommand.ExecuteNonQuery();
            //now the control choices
            myCommand = new MySqlCommand("INSERT INTO ChosenNumbers (n1, n2, n3, n4, n5, n6, date, type, matching) Values (" + cChoicesString + ",'" + date + "','control', '" + cMatched + "')", myConnection);
            myCommand.ExecuteNonQuery();
        }

        public void GetTodaysWinningNumbers()
        {
            string winningUrl = "http://www.txlottery.org/export/sites/lottery/Games/Lotto_Texas/index.html";
            string date = DateTime.Today.ToString("MM/dd/yy"); //gets date in mm/dd/yyyy format
            winningNumbers = new List<string>();
            HtmlWeb htmlWeb = new HtmlWeb();
            HtmlAgilityPack.HtmlDocument htmldoc = htmlWeb.Load(winningUrl);
            //first get the date
            //HtmlNodeCollection winningDate = htmldoc.DocumentNode.SelectNodes("//td[@class='currLotDate']");
            HtmlNodeCollection winningDate = htmldoc.DocumentNode.SelectNodes("//div[@class='large-12 columns']");
            string dateLine = winningDate[2].InnerHtml.Substring(50, 10);//parse the date out of the winning date sentence
            
            //now get the winning numbers
            //HtmlNodeCollection winningNumberNodes = htmldoc.DocumentNode.SelectNodes("//td[@class='currLotWinnum']");
            HtmlNodeCollection winningNumberNodes = htmldoc.DocumentNode.SelectNodes("//ol[@class='winningNumberBalls']//li");
            string[] sep = new string[] {"\n\t\t\t"};
            //List<string> tempNumbers = winningNumberNodes[0].InnerText.Split(sep, StringSplitOptions.None).ToList();
            List<string> tempNumbers = new List<string>();
            tempNumbers.Add(winningNumberNodes[12].InnerText);
            tempNumbers.Add(winningNumberNodes[13].InnerText);
            tempNumbers.Add(winningNumberNodes[14].InnerText);
            tempNumbers.Add(winningNumberNodes[15].InnerText);
            tempNumbers.Add(winningNumberNodes[16].InnerText);
            tempNumbers.Add(winningNumberNodes[17].InnerText);
            
            
            foreach (string num in tempNumbers)
            {
                string newNum = Regex.Replace(num, @"\s+", ""); //remove all extra white spaces
                winningNumbers.Add(newNum);
            }

            //write numbers to database
            //first need to change the current date
            MySqlCommand myCommand = new MySqlCommand("UPDATE WinningNumbers SET current='0' WHERE current='1'", myConnection);
            myCommand.ExecuteNonQuery();
            //now we can write the new 
            string wChoicesString = ChoicesToString(winningNumbers);
            myCommand = new MySqlCommand("INSERT INTO WinningNumbers (w1, w2, w3, w4, w5, w6, date, current) Values (" + wChoicesString + ",'" + date + "','1')", myConnection);
            myCommand.ExecuteNonQuery();
        }

        static void Main(string[] args)
        {
            LottoNumberTool tool = new LottoNumberTool();
            try 
            { 
                //Part 1: Get the number frequency by querying a page from the Texas lotto site and parsing the results
                tool.GetNumberFrequences();

                //Part 2: Get today's winning numbers and write them to the database
                tool.GetTodaysWinningNumbers();

                //Part 3: Pick random numbers based on the weighted query then write them to the database
                tool.PickRandomNumbers();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.ReadLine();
            }
            finally 
            { 
                tool.myConnection.Close();
            }
        }

        //use these for testing. No real use other than that. 
        public static void PrintList(List<string> list)
        {
            for (int i = 0; i < list.Count; i++)
            {

                Console.WriteLine(i + " " + list[i]);
            }
            Console.WriteLine("============================================================");
            Console.ReadLine();
        }
        
        public static void PrintList(List<int> list)
        {
            for (int i = 0; i < list.Count; i++)
            {

                Console.WriteLine(i + " " + list[i]);
            }
            Console.WriteLine("============================================================");
            Console.ReadLine();
        }

        public static void PrintList(List<LottoNumber> list)
        {
            for (int i = 0; i < list.Count; i++)
            {

                Console.WriteLine(i + " " + list[i].number + " " + list[i].occurence);
            }
            Console.WriteLine("============================================================");
            Console.ReadLine();
        }
    }
}
