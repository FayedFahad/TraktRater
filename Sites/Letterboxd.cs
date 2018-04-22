﻿namespace TraktRater.Sites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using Microsoft.VisualBasic.FileIO;
    
    using global::TraktRater.Extensions;
    using global::TraktRater.Logger;
    using global::TraktRater.Settings;
    using global::TraktRater.Sites.API.Letterboxd;
    using global::TraktRater.Sites.Common;
    using global::TraktRater.TraktAPI;
    using global::TraktRater.TraktAPI.DataStructures;
    using global::TraktRater.UI;

    internal class Letterboxd : IRateSite
    {
        #region Variables

        bool mImportCancelled = false;

        readonly bool mImportRatings = false;
        readonly bool mImportWatched = false;
        readonly bool mImportDiary = false;

        readonly string mLetterboxdRatingsFile = null;
        readonly string mLetterboxdWatchedFile = null;
        readonly string mLetterboxdDiaryFile = null;

        #endregion

        #region Constructor

        public Letterboxd(string aRatingsFile, string aWatchedFile, string aDiaryFile)
        {
            mLetterboxdRatingsFile = aRatingsFile;
            mLetterboxdWatchedFile = aWatchedFile;
            mLetterboxdDiaryFile = aDiaryFile;

            mImportRatings = File.Exists(mLetterboxdRatingsFile);
            mImportWatched = File.Exists(mLetterboxdWatchedFile);
            mImportDiary = File.Exists(mLetterboxdDiaryFile);

            Enabled = mImportRatings || mImportWatched || mImportDiary;
        }

        #endregion

        #region IRateSite Members

        public string Name { get { return "Letterboxd"; } }

        public bool Enabled { get; set; }

        public void ImportRatings()
        {

            var lRateItems = new List<Dictionary<string, string>>();
            var lWatchedItems = new List<Dictionary<string, string>>();
            var lDiaryItems = new List<Dictionary<string, string>>();

            mImportCancelled = false;

            #region Parse Ratings CSV
            UIUtils.UpdateStatus("Reading Letterboxd ratings export...");
            if (mImportRatings && !ParseCSVFile(mLetterboxdRatingsFile, out lRateItems))
            {
                UIUtils.UpdateStatus("Failed to parse Letterboxd ratings file!", true);
                Thread.Sleep(2000);
                return;
            }
            if (mImportCancelled) return;
            #endregion

            #region Parse Watched CSV
            UIUtils.UpdateStatus("Reading Letterboxd watched export...");
            if (mImportWatched && !ParseCSVFile(mLetterboxdWatchedFile, out lWatchedItems))
            {
                UIUtils.UpdateStatus("Failed to parse Letterboxd watched file!", true);
                Thread.Sleep(2000);
                return;
            }
            if (mImportCancelled) return;
            #endregion

            #region Parse Diary CSV
            UIUtils.UpdateStatus("Reading Letterboxd diary export...");
            if (mImportDiary && !ParseCSVFile(mLetterboxdDiaryFile, out lDiaryItems))
            {
                UIUtils.UpdateStatus("Failed to parse Letterboxd diary file!", true);
                Thread.Sleep(2000);
                return;
            }
            if (mImportCancelled) return;
            #endregion

            #region Import Rated Movies
            FileLog.Info("Found {0} movie ratings in CSV file", lRateItems.Count);
            if (lRateItems.Any())
            {
                UIUtils.UpdateStatus("Retrieving existing movie ratings from trakt.tv");
                var currentUserMovieRatings = TraktAPI.GetRatedMovies();

                if (currentUserMovieRatings != null)
                {
                    UIUtils.UpdateStatus("Found {0} user movie ratings on trakt.tv", currentUserMovieRatings.Count());
                    // Filter out movies to rate from existing ratings online
                    lRateItems.RemoveAll(m => currentUserMovieRatings.Any(c => c.Movie.Title == m[LetterboxdFieldMapping.cTitle] && c.Movie.Year.ToString() == m[LetterboxdFieldMapping.cYear]));
                }

                UIUtils.UpdateStatus("Importing {0} new movie ratings to trakt.tv", lRateItems.Count());

                if (lRateItems.Count > 0)
                {
                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)lRateItems.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        UIUtils.UpdateStatus("Importing page {0}/{1} Letterboxd rated movies...", i + 1, pages);

                        TraktSyncResponse response = TraktAPI.AddMoviesToRatings(GetRateMoviesData(lRateItems.Skip(i * pageSize).Take(pageSize)));
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Error importing Letterboxd movie ratings to trakt.tv", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Movies.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync Letterboxd ratings for {0} movies as they're not found on trakt.tv!", response.NotFound.Movies.Count);
                            Thread.Sleep(1000);
                        }

                        if (mImportCancelled) return;
                    }
                }
            }
            if (mImportCancelled) return;
            #endregion

            #region Import Watched Movies

            // The Dairy can can include rated and watched items, it also has a watched date
            // if the watched movie exists in the diary use that as a watched date otherwise use date from watched file

            // add to diary any watched films that have been back filled
            foreach (var movie in lWatchedItems)
            {
                if (!lDiaryItems.Exists(d => d[LetterboxdFieldMapping.cTitle] == movie[LetterboxdFieldMapping.cTitle] && d[LetterboxdFieldMapping.cYear] == movie[LetterboxdFieldMapping.cYear]))
                {
                    lDiaryItems.Add(movie);
                }
            }

            if (lDiaryItems.Count > 0)
            {
                // get watched movies from trakt.tv
                UIUtils.UpdateStatus("Requesting watched movies from trakt...");
                var watchedTraktMovies = TraktAPI.GetWatchedMovies();
                if (watchedTraktMovies == null)
                {
                    UIUtils.UpdateStatus("Failed to get watched movies from trakt.tv, skipping watched movie import", true);
                    Thread.Sleep(2000);
                }
                else
                {
                    if (mImportCancelled) return;

                    UIUtils.UpdateStatus("Found {0} watched movies on trakt", watchedTraktMovies.Count());
                    UIUtils.UpdateStatus("Filtering out watched movies that are already watched on trakt.tv");

                    lDiaryItems.RemoveAll(d => watchedTraktMovies.FirstOrDefault(t => t.Movie.Title == d[LetterboxdFieldMapping.cTitle] && t.Movie.Year.ToString() == d[LetterboxdFieldMapping.cYear]) != null);

                    UIUtils.UpdateStatus("Importing {0} Letterboxd movies as watched...", lDiaryItems.Count);

                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)lDiaryItems.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        UIUtils.UpdateStatus("Importing page {0}/{1} Letterboxd movies as watched...", i + 1, pages);

                        var response = TraktAPI.AddMoviesToWatchedHistory(GetSyncWatchedMoviesData(lDiaryItems.Skip(i * pageSize).Take(pageSize).ToList()));
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Failed to send watched status for Letterboxd movies to trakt.tv", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Movies.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync Letterboxd watched states for {0} movies as they're not found on trakt.tv!", response.NotFound.Movies.Count);
                            Thread.Sleep(1000);
                        }
                        if (mImportCancelled) return;
                    }
                }
            }
            #endregion

            return;
        }

        public void Cancel()
        {
            mImportCancelled = true;
        }

        #endregion

        #region Private Methods
        
        private bool ParseCSVFile(string aFilename, out List<Dictionary<string, string>> aParsedCSV)
        {
            aParsedCSV = new List<Dictionary<string, string>>();

            if (!File.Exists(aFilename)) return false;

            string[] lFieldHeadings = new string[] { };
            int lRecordNumber = 0;

            try
            {
                var lParser = new TextFieldParser(aFilename) { TextFieldType = FieldType.Delimited };
                lParser.SetDelimiters(",");
                while (!lParser.EndOfData)
                {
                    lRecordNumber++;
                    // processing fields in row
                    string[] lFields = lParser.ReadFields();

                    // get header fields
                    // line number increments after first read
                    if (lRecordNumber == 1)
                    {
                        lFieldHeadings = lFields;
                        continue;
                    }

                    if (lFields.Count() != lFieldHeadings.Count()) continue;

                    // get each field value
                    int lIndex = 0;
                    var lExportItem = new Dictionary<string, string>();

                    foreach (string field in lFields)
                    {
                        lExportItem.Add(lFieldHeadings[lIndex], field);
                        lIndex++;
                    }
                    
                    // check no invalid entries                                    
                    if (!string.IsNullOrEmpty(lExportItem[LetterboxdFieldMapping.cTitle]))
                    {
                        // add to list of items
                        aParsedCSV.Add(lExportItem);
                    }
                }
                lParser.Close();
            }
            catch
            {
                return false;
            }
            return true;
        }

        private TraktMovieRatingSync GetRateMoviesData(IEnumerable<Dictionary<string, string>> aMovies)
        {
            var lTraktMovies = new List<TraktMovieRating>();

            lTraktMovies.AddRange(from movie in aMovies
                                 select new TraktMovieRating
                                 {
                                     Title = movie[LetterboxdFieldMapping.cTitle],
                                     Year = movie[LetterboxdFieldMapping.cYear].ToYear(),
                                     Rating = (int)Math.Ceiling(float.Parse(movie[LetterboxdFieldMapping.cRating], CultureInfo.InvariantCulture.NumberFormat) * 2),
                                     RatedAt = GetDateAdded(movie)
                                 });

            var lMovieRateData = new TraktMovieRatingSync
            {
                movies = lTraktMovies
            };

            return lMovieRateData;
        }

        private TraktMovieWatchedSync GetSyncWatchedMoviesData(List<Dictionary<string, string>> aMovies)
        {
            var lTraktMovies = new List<TraktMovieWatched>();

            lTraktMovies.AddRange(from movie in aMovies
                                 select new TraktMovieWatched
                                 {
                                     Title = movie[LetterboxdFieldMapping.cTitle],
                                     Year = movie[LetterboxdFieldMapping.cYear].ToYear(),
                                     WatchedAt = GetWatchedDate(movie)
                                 });

            var lMovieData = new TraktMovieWatchedSync
            {
                Movies = lTraktMovies
            };

            return lMovieData;
        }

        private string GetDateAdded(Dictionary<string, string> aItem)
        {
            var lResult = DateTime.UtcNow;

            if (aItem.ContainsKey(LetterboxdFieldMapping.cDateAdded))
            {
                DateTime.TryParse(aItem[LetterboxdFieldMapping.cDateAdded], CultureInfo.InvariantCulture, DateTimeStyles.None, out lResult);
            }

            return lResult.ToString().ToISO8601();
        }

        private string GetWatchedDate(Dictionary<string, string> aItem)
        {
            var lResult = DateTime.UtcNow;

            // check if diary field exists for Watched Date
            if (aItem.ContainsKey(LetterboxdFieldMapping.cWatchedDate))
            {
                DateTime.TryParse(aItem[LetterboxdFieldMapping.cWatchedDate], CultureInfo.InvariantCulture, DateTimeStyles.None, out lResult);
            }
            else
            {
                // fallback to the date it was added into the watched file
                // use release day if setting is enabled
                return AppSettings.WatchedOnReleaseDay ? "released" : GetDateAdded(aItem);
            }

            return lResult.ToString().ToISO8601();
        }

        #endregion
    }
}
