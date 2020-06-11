﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Xml;
using System.Globalization;

namespace FileLogAnalyzer
{

    public partial class AnalyzeFileLogForm : Form
    {
        private int trackMaxOpenedFileCount;
        private int outputReportCount;
        private int openedButNotClosedReportCount;
        private int closedButTryingWriteCount;
        private int handledByMuliTreadCount;

        public AnalyzeFileLogForm()
        {
            InitializeComponent();
        }

        private void btStart_Click(object sender, EventArgs e)
        {
            FileOperationLogBuilder logBuilder = new FileOperationLogBuilder();
            Dictionary<String, TrackOpenFileItem> trackOpenFileDictionary = new Dictionary<string, TrackOpenFileItem>();
            StreamReader logReader = null;

            // 통계정보 
            trackMaxOpenedFileCount = 0;
            outputReportCount = 0;
            openedButNotClosedReportCount = 0;
            closedButTryingWriteCount = 0;
            handledByMuliTreadCount = 0;

            // 로그 파일 전체 읽기
            string logPath = GetLogPathFromUser();
            if (logPath == null)
            {
                return;
            }

            logPathView.Text = "Log Path :" + logPath;

            // 로그 파일 구조화
            try
            {
                logReader = new StreamReader(logPath);

                // 첫째 줄에 있는 헤더 정보는 패스
                string rawLog = logReader.ReadLine();

                while (logReader.Peek() >= 0)
                {
                    // 파일 연산 로그 읽기
                    rawLog = logReader.ReadLine();

                    FileOperateLog fileOperateLog = new FileOperateLog();

                    // 연산 로그 구조화 
                    if (logBuilder.Build(rawLog, fileOperateLog) == false)
                    {
                        continue;
                    }

                    // 분석 대상 파일여부 확인 (폴더, OS파일, exe, dll 분석 대상에서 제외) 
                    if (!FileChecker.IsTargetFileForAnalysis(fileOperateLog.FilePath, fileOperateLog.Result, fileOperateLog.Detail))
                    {
                        continue;
                    }

                    // 각 파일 연산 별 처리
                    if (fileOperateLog.Type == "CreateFile")
                    {
                        ProcessOpenFileLog(trackOpenFileDictionary, fileOperateLog);
                    }
                    else if (fileOperateLog.Type == "CloseFile")
                    {
                        ProcessCloseFileLog(trackOpenFileDictionary, fileOperateLog);
                    }
                    else if (fileOperateLog.Type == "WriteFile")
                    {
                        ProcessWriteFileLog(trackOpenFileDictionary, fileOperateLog);
                    }
                    else
                    {
                        // Do not process others.
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine("The process failed: {0}", exception.ToString());
            }
            finally
            {
                if (logReader != null)
                {
                    logReader.Dispose();
                }
            }

            // 열고 닫지 않은 파일 확인
            foreach(var item in trackOpenFileDictionary.ToList())
            {
                opendButNotClosedReporter.AppendText("- " + item.Value.ToString() + "\r\n");
                openedButNotClosedReportCount++;
            }

            // 통계정보 출력
            outputView.AppendText("- 동시에 열린 파일 개수 최대치 : " + trackMaxOpenedFileCount + "\r\n");
            outputReportCount++;

            outputView.AppendText("Total = " + outputReportCount + "\r\n");
            opendButNotClosedReporter.AppendText("Total = " + openedButNotClosedReportCount + "\r\n");
            closedButTryingWriteReport.AppendText("Total = " + closedButTryingWriteCount + "\r\n");
            handledByMulithreadReport.AppendText("Total = " + handledByMuliTreadCount + "\r\n");

        }


        private void ProcessOpenFileLog(Dictionary<String, TrackOpenFileItem> trackOpenFileDictionary, FileOperateLog fileOperateLog)
        {
            TrackOpenFileItem newTrackItem = new TrackOpenFileItem(fileOperateLog);
            TrackOpenFileItem alreadyTrackItem;

            if (trackOpenFileDictionary.TryGetValue(fileOperateLog.FilePath, out alreadyTrackItem) == true)
            {
                if (newTrackItem.ThreadId != alreadyTrackItem.ThreadId)
                {
                    handledByMulithreadReport.AppendText("- 기존에 열린 파일을 다른 쓰레드에서 다시 엽니다 - " + newTrackItem.ToString() + "\r\n");
                    handledByMuliTreadCount++;
                }

                alreadyTrackItem.OpenCount++;
                if (alreadyTrackItem.OpenCount > 1)
                {
                    outputView.AppendText("- 같은 파일을 2번 이상 엽니다 - " + newTrackItem.ToString() + "\r\n");
                    outputReportCount++;
                }
            }
            else
            {
                trackOpenFileDictionary.Add(newTrackItem.FilePath, newTrackItem);

                // 열린 파일 최대로 많은 경우 추적
                if (trackOpenFileDictionary.Count > trackMaxOpenedFileCount)
                {
                    trackMaxOpenedFileCount = trackOpenFileDictionary.Count;

                    // 열린 파일이 500개 이상이면 레포팅
                    if (trackMaxOpenedFileCount > 500)
                    {
                        outputView.AppendText("- 500개 이상의 파일을 동시에 엽니다 \r\n");
                    }
                }

            }
        }

        private void ProcessCloseFileLog(Dictionary<String, TrackOpenFileItem> trackOpenFileDictionary, FileOperateLog fileOperateLog)
        {
            TrackOpenFileItem alreadyTrackItem;

            if (trackOpenFileDictionary.TryGetValue(fileOperateLog.FilePath, out alreadyTrackItem) == true)
            {
                if (fileOperateLog.ThreadId != alreadyTrackItem.ThreadId)
                {
                    handledByMulithreadReport.AppendText("- 기존에 다른 쓰레드에서 열린 파일을 닫습니다 - " + fileOperateLog.ToString() + "\r\n");
                    handledByMuliTreadCount++;
                }

                alreadyTrackItem.OpenCount--;
                if (alreadyTrackItem.OpenCount <= 0)
                {
                    trackOpenFileDictionary.Remove(fileOperateLog.FilePath);
                }
            }
            else
            {
                outputView.AppendText("- 열리지 않은 파일이 닫힘" + fileOperateLog.ToString() + "\r\n");
            }
        }

        private void ProcessWriteFileLog(Dictionary<String, TrackOpenFileItem> trackOpenFileDictionary, FileOperateLog fileOperateLog)
        {
            TrackOpenFileItem alreadyTrackItem;

            // 닫힌 파일에 대한 쓰기 시도 추적
            if (trackOpenFileDictionary.TryGetValue(fileOperateLog.FilePath, out alreadyTrackItem) == false)
            {
                closedButTryingWriteReport.AppendText("- " + fileOperateLog.ToString() + "\r\n");
                closedButTryingWriteCount++;
            }
            else
            {
                // 서로 다른 쓰레드에서 접근하는 파일 추적 
                if (fileOperateLog.ThreadId != alreadyTrackItem.ThreadId)
                {
                    handledByMulithreadReport.AppendText("- " + fileOperateLog.ToString() + "\r\n");
                    handledByMuliTreadCount++;
                }
            }
        }

        private string GetLogPathFromUser()
        {
            string fileContent = string.Empty;
            string filePath = string.Empty;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = @"D:\Working\7.3m C&M Manage\이슈\서버 비정상 종료\로그";
                openFileDialog.Filter = "csv files (*.csv)|*.csv|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //Get the path of specified file
                    return openFileDialog.FileName;
                }
                else
                {
                    return null;
                }
            }
        }


        private void btClearView_Click(object sender, EventArgs e)
        {
            outputView.Clear();
            closedButTryingWriteReport.Clear();
            handledByMulithreadReport.Clear();
            opendButNotClosedReporter.Clear();
        }
    }
}
