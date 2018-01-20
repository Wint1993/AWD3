using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge;
using AForge.Neuro;
using AForge.Neuro.Learning;
using AForge.Controls;
using System.Threading;
using System.IO;

namespace AWD
{
    public partial class Form1 : Form
    {
        
        public Form1()
        {
            InitializeComponent();
            graphSettings();
            updateSettings();
        }

        private void graphSettings()
        {
            chart.AddDataSeries("data", Color.Red, Chart.SeriesType.Dots, 5);
            chart.AddDataSeries("solution", Color.Blue, Chart.SeriesType.Line, 1);
            chart.AddDataSeries("omittedInitialPoints", Color.LightGray, Chart.SeriesType.Line, 1, false);
            chart.AddDataSeries("omittedEndPoints", Color.Gray, Chart.SeriesType.Line, 1, false);
        }

        ActivationNetwork network;
        BackPropagationLearning teacher;

        private double learnRatio = 0.1;
        private double[] inputData = null;
        private double[,] displayData = null;
        private double[,] initialDelimiter = new double[2, 2] { { 0, 0 }, { 0, 0 } };
        private double[,] closingDelimiter = new double[2, 2] { { 0, 0 }, { 0, 0 } };

        private int MAXOMITTEDENDPOINTS = 14;
        private int MINOMITTEDINITIALPOINTS = 15;

        private int amountOfOmittedInitialPoints = 1;
        private int amountOfOmittedEndsPoints = 1;
        private int iterations = 10000;
        private int dataQuantity = 252;

        private Thread thread = null;
        private volatile bool needToStop = false;


        private void button1_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                StreamReader rd = null;
               
                // Wczytywanie danych
                double[] tmp = new double[dataQuantity];

                try
                {
                    rd = File.OpenText(openFileDialog1.FileName);
                    string str = null;
                    int i = 0;

                    // Wczytywanie danych
                    while ((i < dataQuantity) && ((str = rd.ReadLine()) != null))
                    {
                        // Parsowanie danych
                        tmp[i] = double.Parse(str);

                        i++;
                    }

                    // Ustawianie danych
                    inputData = new double[i];
                    displayData = new double[i, 2];
                    Array.Copy(tmp, 0, inputData, 0, i);
                    for (int j = 0; j < i; j++)
                    {
                        displayData[j, 0] = j;
                        displayData[j, 1] = inputData[j];
                    }
                }
                finally
                {
                    if (rd != null)
                        rd.Close();
                }

                // Aktualizacjia kolumn danych oraz wykresu
                updateDataList();
                chart.RangeX = new Range(0, inputData.Length - 1);
                chart.UpdateDataSeries("data", displayData);
                chart.UpdateDataSeries("solution", null);
                // Ustawienie ograniczen 
                setDelimiters();
             
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            // Czyszczenie oraz ustawienie początkowych danych
            clearSolution();
            try
            {
                learnRatio = Math.Max(0.00001, Math.Min(1, double.Parse(textBox1.Text)));
            }
            catch
            {
                learnRatio = 0.1;
            }  
         
            updateSettings();
            needToStop = false;
            thread = new Thread(new ThreadStart(findSolution));
            thread.Start();
        }

        private void clearSolution()
        {
            for (int j = 0, n = inputData.Length; j < n; j++)
            {
                if (listView1.Items[j].SubItems.Count > 1)
                    listView1.Items[j].SubItems.RemoveAt(1);
            }
        }

        private delegate void SetTextCallback(System.Windows.Forms.Control control, string text);
        private delegate void AddSubItemCallback(System.Windows.Forms.ListView control, int item, string subitemText);

        // Funkcja służy do aktualizowania danych zmieniających sie w cz
        private void updateText(System.Windows.Forms.Control c, string txt)
        {
            if (c.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(updateText);
                Invoke(d, new object[] { c, txt });
            }
            else
            {
                c.Text = txt;
            }
        }

        private void addSolutionToEstimatedColumn(System.Windows.Forms.ListView control, int item, string itemTxt)
        {
            if (control.InvokeRequired)
            {
                AddSubItemCallback d = new AddSubItemCallback(addSolutionToEstimatedColumn);
                Invoke(d, new object[] { control, item, itemTxt });
            }
            else
            {
                control.Items[item].SubItems.Add(itemTxt);
            }
        }

        // Aktualizowanie parametrów wejściowych
        private void updateSettings()
        {
            textBox1.Text = learnRatio.ToString();
            textBox3.Text = amountOfOmittedInitialPoints.ToString();
            textBox4.Text = amountOfOmittedEndsPoints.ToString();
        }

        // Ustawianie lini progowych (ograniczających pole działa sieci neuronowej) 
        private void setDelimiters()
        {
            initialDelimiter[0, 0] = initialDelimiter[1, 0] = amountOfOmittedInitialPoints;
            initialDelimiter[0, 1] = chart.RangeY.Min;
            initialDelimiter[1, 1] = chart.RangeY.Max;
            chart.UpdateDataSeries("omittedInitialPoints", initialDelimiter);
            closingDelimiter[0, 0] = closingDelimiter[1, 0] = inputData.Length - 1 - amountOfOmittedEndsPoints;
            closingDelimiter[0, 1] = chart.RangeY.Min;
            closingDelimiter[1, 1] = chart.RangeY.Max;
            chart.UpdateDataSeries("omittedEndPoints", closingDelimiter);
        }

        // Aktualizacja danych wejsciowych
        private void updateDataList()
        {
            listView1.Items.Clear();
            for (int i = 0, n = inputData.GetLength(0); i < n; i++)
            {
                listView1.Items.Add(inputData[i].ToString());
            }
        }

        // Ustawianie lini progowej (ograniczającej) początkowej
        private void updateOmittedInitialPointsQuantity()
        {
            if (inputData != null)
            {
                // Ustawienie nowej  lini progowej
                try
                {
                    amountOfOmittedInitialPoints = Math.Max(1, Math.Min(MINOMITTEDINITIALPOINTS, int.Parse(textBox3.Text)));
                }
                catch
                {
                    amountOfOmittedInitialPoints = 1;
                }
                // Warunek, który musi być spełniony - jeżeli wyjedziemy linią progową za obszar końcowy danych
                if (amountOfOmittedInitialPoints >= inputData.Length)
                   amountOfOmittedInitialPoints = 1;
                // Aktualizacja ograniczników
                setDelimiters();
            }
        }

        // Ustawianie lini progowej (ograniczającej) końcowej
        private void updateOmittedEndsPointsQuantity()
        {
            if (inputData != null)
            {
                // Ustawienie nowej  lini progowej
                try
                {
                    amountOfOmittedEndsPoints = Math.Max(1, Math.Min(MAXOMITTEDENDPOINTS, int.Parse(textBox4.Text)));
                }
                catch
                {
                    amountOfOmittedEndsPoints = 1;
                }
                // Warunek, który musi być spełniony - jeżeli wyjedziemy linią progową za obszar początkowy danych
                if (inputData.Length - amountOfOmittedEndsPoints - 1 < amountOfOmittedInitialPoints)
                    amountOfOmittedEndsPoints = 1;
                // Aktualizacja ograniczników
                setDelimiters();
            }
        }

        void findSolution()
        {
            // Ilość danych zbioru uczącego
            int quantitySamples = inputData.Length - amountOfOmittedEndsPoints - amountOfOmittedInitialPoints;
            // Różnica pomiędzy wartością największą a najmniejszą ze zbioru uczącego
            double diffrenceMinMaxValue = chart.RangeY.Length;
            // Współczynnik transformacji danych - factor - wartość oszacowania odchylenia standardowego populacji dla całkowitej zmienności
            // Jest on użyty po to, żeby przy normalizacji danych nie przekroczyć zakresu -1 do 1
            double dtc = 1.7 / diffrenceMinMaxValue;
            //Najmniejsza wartość zbioru uczącego
            double yMin = chart.RangeY.Min;
            // Przygotowanie danych uczących
            double[][] inputTmpData = new double[quantitySamples][];
            double[][] outputTmpData = new double[quantitySamples][];

            // Normalizacja danych z zakresu -1 do 1
            for (int i = 0; i < quantitySamples; i++)
            {
                // Ustawienie nowych danych, nie włączając w to danych pominiętych przez linie ograniczające,
                // przypisanie danych do tablicy outputTmpData
                inputTmpData[i] = new double[amountOfOmittedInitialPoints];
                outputTmpData[i] = new double[1];

                //  Ustawienie danych wejściowych - normalizacja danych
                for (int j = 0; j < amountOfOmittedInitialPoints; j++)
                {
                    inputTmpData[i][j] = (inputData[i + j] - yMin) * dtc - 0.85;
                }
                // Ustawienie danych końcowych
                outputTmpData[i][0] = (inputData[i + amountOfOmittedInitialPoints] - yMin) * dtc - 0.85;
            }

            createMultiLayerNeuralNetwork(out network, out teacher);
            int iteration = 1;

            // Tablice rozwiązań
            int solutionSize = inputData.Length - amountOfOmittedInitialPoints;
            double[,] solution = new double[solutionSize, 2];
            double[] networkInput = new double[amountOfOmittedInitialPoints];

            // Obliczanie wartosci, które będą użyte do funkcji rozwiązującej - do przypisania danych dla tablicy estymowanej
            for (int j = 0; j < solutionSize; j++)
            {
                solution[j, 0] = j + amountOfOmittedInitialPoints;
            }

            // Pętla rozpoczynająca uczenie 
            while (!needToStop)
            {
                // Uruchamiamy epokę procedury uczenia
                double error = teacher.RunEpoch(inputTmpData, outputTmpData) / quantitySamples;
               
                // Obliczanie wyniku oraz błędu uczenia i błędu predykcji
                double learningError = 0.0;
                double predictionError = 0.0;
              
                for (int i = 0, n = inputData.Length - amountOfOmittedInitialPoints; i < n; i++)
                {
                    // Wstawianie wartości bieżących(dla wartości od lini progowej poczatkowej) podlegających normalizacji
                    for (int j = 0; j < amountOfOmittedInitialPoints; j++)
                    {
                        networkInput[j] = (inputData[i + j] - yMin) * dtc - 0.85;
                    }

                    // Ocenianie funkcji
                    solution[i, 1] = (network.Compute(networkInput)[0] + 0.85) / dtc + yMin;

                    //Obliczanie błędu predykcji
                    if (i >= n - amountOfOmittedEndsPoints)
                    {
                        predictionError += Math.Abs(solution[i, 1] - inputData[amountOfOmittedInitialPoints + i]);
                    }
                    else
                    {
                        learningError += Math.Abs(solution[i, 1] - inputData[amountOfOmittedInitialPoints + i]);
                    }
                }
                // Aktualizowanie wykresu
                chart.UpdateDataSeries("solution", solution);

                // Aktualizowanie danych - iteracji, błędu uczenia oraz blędu predykcji w czasie iteracji od 1 do 1000
                updateText(textBox5, iteration.ToString());
                updateText(textBox6, learningError.ToString("F3"));
                updateText(textBox7, predictionError.ToString("F3"));
                iteration++;

                // Sprawdzenie czy trzeba się zatrzymać
                if ((iterations != 0) && (iteration > iterations))
                    break;
            }

            // Dodanie wyników do estymowanej kolumny wynikowej
            for (int j = amountOfOmittedInitialPoints, k = 0, n = inputData.Length; j < n; j++, k++)
            {
                addSolutionToEstimatedColumn(listView1, j, solution[k, 1].ToString());
            }

        }


        // Tworzenie sieci neuronowej
        private void createMultiLayerNeuralNetwork(out ActivationNetwork network, out BackPropagationLearning teacher)
        {
            network = new ActivationNetwork(
                new BipolarSigmoidFunction(),
                amountOfOmittedInitialPoints, (amountOfOmittedInitialPoints * 2)+1, 1);
            teacher = new BackPropagationLearning(network);
            teacher.LearningRate = learnRatio;
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            updateOmittedInitialPointsQuantity();
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            updateOmittedEndsPointsQuantity();
        }
    }
}
