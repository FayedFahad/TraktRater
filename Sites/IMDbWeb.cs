﻿namespace TraktRater.Sites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;

    using global::TraktRater.Logger;
    using global::TraktRater.Settings;
    using global::TraktRater.Sites.API.IMDb;
    using global::TraktRater.Sites.Common;
    using global::TraktRater.TraktAPI;
    using global::TraktRater.TraktAPI.DataStructures;
    using global::TraktRater.UI;
    using global::TraktRater.Web;

    class IMDbWeb : IRateSite
    {
        bool importCancelled = false;
        private readonly string username;

        public IMDbWeb(string userName, bool enabled)
        {
            Enabled = !string.IsNullOrEmpty(userName) && enabled;
            username = userName;
        }

        #region IRateSite

        public string Name
        {
            get { return "IMDbWeb"; }
        }

        public bool Enabled
        { get; set; }

        public void ImportRatings()
        {
            importCancelled = false;

            List<Dictionary<string, string>> watchlistMovies = new List<Dictionary<string, string>>();
            List<Dictionary<string, string>> watchlistShows = new List<Dictionary<string, string>>();
            List<Dictionary<string, string>> ratedItems = new List<Dictionary<string, string>>();
            List<Dictionary<string, string>> watchlistItems = new List<Dictionary<string, string>>();

            #region Download Data
            UIUtils.UpdateStatus("Requesting ratings from IMDb.com...");

            int count;
            int movieIndex = 1;
            int movieIncrement = 250;

            #region Ratings
            do
            {
                count = ratedItems.Count;
                UIUtils.UpdateStatus("Requesting ratings {0} - {1}, Total Results: {2}", movieIndex, (movieIncrement + movieIndex - 1), count);
                string url = "http://www.imdb.com/user/" + username + "/ratings?start=" + movieIndex + "&view=compact";                
                string response = TraktWeb.Transmit(url, null, false);
                if (response == null) break;
                int begin = 0;

                // only log response when set to trace as it's very verbose in this case
                if (AppSettings.LogSeverityLevel >= AppSettings.LoggingSeverity.Trace)
                    FileLog.Trace("Response: {0}", response); 

                while ((begin = response.IndexOf("<tr data-item-id", begin)) > 0)
                {
                    var rateItem = new Dictionary<string, string>();
                    string sub = response.Substring(begin, response.IndexOf("</tr>", begin) - begin);

                    Regex reg = new Regex("<td class=\"title[^\"]*\"><a href=\"/title/(?<cIMDbID>tt\\d+)/[^\"]*\">(?<cTitle>[^<]+)</a>(?:\\s*<br>\\s*Episode:\\s*<a href=\"/title/(?<cEpisodeID>tt\\d+)/[^\"]*\">(?<cEpisodeTitle>[^<]+)</a>)?</td>");

                    // Get IMDb ID
                    var find = reg.Match(sub);
                    rateItem.Add(IMDbFieldMapping.cIMDbID, find.Groups["cIMDbID"].ToString());
                    
                    // Get Title
                    // If it's a TV Episode then include both show and episode title
                    if (!string.IsNullOrEmpty(find.Groups["cEpisodeTitle"].ToString()))
                        rateItem.Add(IMDbFieldMapping.cTitle, string.Format("{0}: {1}", find.Groups["cTitle"], find.Groups["cEpisodeTitle"]));
                    else
                        rateItem.Add(IMDbFieldMapping.cTitle, find.Groups["cTitle"].ToString());

                    // Get User Rating
                    reg = new Regex("<td class=\"your_ratings\">\\n    <a>([1-9][0-9]{0,1})</a>\\n</td>");
                    find = reg.Match(sub);
                    rateItem.Add(IMDbFieldMapping.cRating, find.Groups[1].ToString());

                    // Get Year
                    reg = new Regex("<td class=\"year\">([1-2][0-9]{3})</td>");
                    find = reg.Match(sub);
                    rateItem.Add(IMDbFieldMapping.cYear, find.Groups[1].ToString());

                    // Get Type
                    reg = new Regex("<td class=\"title_type\"> (.*)</td>");
                    find = reg.Match(sub);
                    if (find.Groups[1].ToString() == string.Empty)
                        rateItem.Add(IMDbFieldMapping.cType, "Feature Film");
                    else
                        rateItem.Add(IMDbFieldMapping.cType, find.Groups[1].ToString());

                    // Set provider to web or csv
                    rateItem.Add(IMDbFieldMapping.cProvider, "web");

                    ratedItems.Add(rateItem);

                    begin += 10;
                }
                // fetch next page
                movieIndex += movieIncrement;
            }
            while (count < ratedItems.Count);
            #endregion

            #region Watchlist
            if (AppSettings.IMDbSyncWatchlist)
            {
                UIUtils.UpdateStatus("Reading IMDb watchlist from web...");
                movieIndex = 1;
                movieIncrement = 100;

                do
                {
                    count = watchlistItems.Count;
                    UIUtils.UpdateStatus("Requesting watchlist items {0} - {1}, Total Results: {2}", movieIndex, (movieIncrement + movieIndex - 1), count);
                    string url = "http://www.imdb.com/user/" + username + "/watchlist?start=" + movieIndex + "&view=compact";
                    string response = TraktWeb.Transmit(url, null, false);
                    if (response == null) break;
                    int begin = 0;

                    // only log response when set to trace as it's very verbose in this case
                    if (AppSettings.LogSeverityLevel >= AppSettings.LoggingSeverity.Trace)
                        FileLog.Trace("Response: {0}", response); 

                    if (response == null) continue;

                    while ((begin = response.IndexOf("<tr data-item-id", begin)) > 0)
                    {
                        var watchListItem = new Dictionary<string, string>();
                        var sub = response.Substring(begin, response.IndexOf("</tr>", begin) - begin);

                        Regex reg = new Regex("<td class=\"title[^\"]*\"><a href=\"/title/(?<cIMDbID>tt\\d+)/[^\"]*\">(?<cTitle>[^<]+)</a>(?:\\s*<br>\\s*Episode:\\s*<a href=\"/title/(?<cEpisodeID>tt\\d+)/[^\"]*\">(?<cEpisodeTitle>[^<]+)</a>)?</td>");

                        // Get IMDb ID
                        var find = reg.Match(sub);
                        watchListItem.Add(IMDbFieldMapping.cIMDbID, find.Groups["cIMDbID"].ToString());

                        // Get Title
                        // If it's a TV Episode then include both show and episode title
                        if (!string.IsNullOrEmpty(find.Groups["cEpisodeTitle"].ToString()))
                            watchListItem.Add(IMDbFieldMapping.cTitle, string.Format("{0}: {1}", find.Groups["cTitle"], find.Groups["cEpisodeTitle"]));
                        else
                            watchListItem.Add(IMDbFieldMapping.cTitle, find.Groups["cTitle"].ToString());

                        // Get Year
                        reg = new Regex("<td class=\"year\">([1-2][0-9]{3})</td>");
                        find = reg.Match(sub);
                        watchListItem.Add(IMDbFieldMapping.cYear, find.Groups[1].ToString());

                        // Get Type
                        reg = new Regex("<td class=\"title_type\"> (.*)</td>");
                        find = reg.Match(sub);
                        if (find.Groups[1].ToString() == string.Empty)
                            watchListItem.Add(IMDbFieldMapping.cType, "Feature Film");
                        else
                            watchListItem.Add(IMDbFieldMapping.cType, find.Groups[1].ToString());

                        // Set provider to web or csv
                        watchListItem.Add(IMDbFieldMapping.cProvider, "web");

                        watchlistItems.Add(watchListItem);
                        
                        begin += 10;
                    }
                    // fetch next page
                    movieIndex += movieIncrement;
                }
                while (count < watchlistItems.Count);
            }
            #endregion
            #endregion

            if (importCancelled) return;

            #region Sync Ratings
            #region Movies
            var movies = ratedItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Movie && !string.IsNullOrEmpty(r[IMDbFieldMapping.cRating])).ToList();
            if (movies.Any())
            {
                UIUtils.UpdateStatus("Retrieving existing movie ratings from trakt.tv");
                var currentUserMovieRatings = TraktAPI.GetRatedMovies();

                if (currentUserMovieRatings != null)
                {
                    UIUtils.UpdateStatus(string.Format("Found {0} user movie ratings on trakt.tv", currentUserMovieRatings.Count()));
                    // Filter out movies to rate from existing ratings online
                    movies.RemoveAll(m => currentUserMovieRatings.Any(c => c.Movie.Ids.ImdbId == m[IMDbFieldMapping.cIMDbID]));
                }

                UIUtils.UpdateStatus("Importing {0} new IMDb movie ratings to trakt.tv", movies.Count());

                if (movies.Count > 0)
                {
                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)movies.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        UIUtils.UpdateStatus("Importing page {0}/{1} IMDb rated movies...", i + 1, pages);

                        TraktSyncResponse response = TraktAPI.AddMoviesToRatings(Helper.GetRateMoviesData(movies.Skip(i * pageSize).Take(pageSize)));
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Error importing IMDb movie ratings to trakt.tv", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Movies.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync ratings for {0} IMDb movies as they're not found on trakt.tv!", response.NotFound.Movies.Count);
                            Thread.Sleep(1000);
                        }

                        if (importCancelled) return;
                    }
                }
            }
            if (importCancelled) return;
            #endregion

            #region TV Shows
            var shows = ratedItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Show && !string.IsNullOrEmpty(r[IMDbFieldMapping.cRating])).ToList();
            if (shows.Any())
            {
                UIUtils.UpdateStatus("Retrieving existing tv show ratings from trakt.tv");
                var currentUserShowRatings = TraktAPI.GetRatedShows();

                if (currentUserShowRatings != null)
                {
                    UIUtils.UpdateStatus("Found {0} user tv show ratings on trakt.tv", currentUserShowRatings.Count());
                    // Filter out shows to rate from existing ratings online
                    shows.RemoveAll(s => currentUserShowRatings.Any(c => (c.Show.Ids.ImdbId == s[IMDbFieldMapping.cIMDbID]) || (c.Show.Title == s[IMDbFieldMapping.cTitle] && c.Show.Year.ToString() == s[IMDbFieldMapping.cYear])));
                }

                if (shows.Count > 0)
                {
                    UIUtils.UpdateStatus("Importing {0} new IMDb tv show ratings to trakt.tv", shows.Count());

                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)shows.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        UIUtils.UpdateStatus("Importing page {0}/{1} IMDb rated shows...", i + 1, pages);

                        TraktSyncResponse response = TraktAPI.AddShowsToRatings(Helper.GetRateShowsData(shows.Skip(i * pageSize).Take(pageSize)));
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Error importing IMDb show ratings to trakt.tv", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Shows.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync ratings for {0} IMDb shows as they're not found on trakt.tv!", response.NotFound.Shows.Count);
                            Thread.Sleep(1000);
                        }

                        if (importCancelled) return;
                    }
                }
            }
            if (importCancelled) return;
            #endregion

            #region Episodes
            var imdbEpisodes = new List<IMDbEpisode>();
            var episodes = ratedItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Episode).ToList();
            if (episodes.Any())
            {
                // we can't rely on the imdb id as trakt most likely wont have the info for episodes

                // search and cache all series info needed for syncing
                // use the tvdb API to first search for each unique series name
                // then GetSeries by TVDb ID to get a list of all episodes
                // each episode will have TVDb ID which we can use for syncing.

                imdbEpisodes.AddRange(episodes.Select(Helper.GetIMDbEpisodeFromTrakt).Where(imdbEpisode => imdbEpisode != null));

                UIUtils.UpdateStatus("Retrieving existing tv episode ratings from trakt.tv");
                var currentUserEpisodeRatings = TraktAPI.GetRatedEpisodes();

                if (currentUserEpisodeRatings != null)
                {
                    UIUtils.UpdateStatus("Found {0} user tv episode ratings on trakt.tv", currentUserEpisodeRatings.Count());

                    // Filter out episodes to rate from existing ratings online
                    imdbEpisodes.RemoveAll(e => currentUserEpisodeRatings.Any(c => c.Episode.Ids.Trakt == e.TraktId));
                }

                UIUtils.UpdateStatus("Importing {0} episode ratings to trakt.tv", imdbEpisodes.Count());

                if (imdbEpisodes.Count > 0)
                {
                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)imdbEpisodes.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        var episodesRated = Helper.GetTraktEpisodeRateData(imdbEpisodes.Skip(i * pageSize).Take(pageSize));

                        UIUtils.UpdateStatus("Importing page {0}/{1} IMDb rated episodes...", i + 1, pages);

                        var response = TraktAPI.AddsEpisodesToRatings(episodesRated);
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Error importing IMDb episodes ratings to trakt.tv", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Episodes.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync ratings for {0} IMDb episodes as they're not found on trakt.tv!", response.NotFound.Episodes.Count);
                            Thread.Sleep(1000);
                        }

                        if (importCancelled) return;
                    }
                }
            }
            if (importCancelled) return;
            #endregion
            #endregion

            #region Mark as Watched
            IEnumerable<TraktMoviePlays> watchedTraktMovies = null;

            if (AppSettings.MarkAsWatched)
            {
                #region Movies
                // compare all movies rated against what's not watched on trakt
                movies = ratedItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Movie && !string.IsNullOrEmpty(r[IMDbFieldMapping.cRating])).ToList();
                if (movies.Count > 0)
                {
                    // get watched movies from trakt.tv
                    UIUtils.UpdateStatus("Requesting watched movies from trakt...");
                    watchedTraktMovies = TraktAPI.GetWatchedMovies();
                    if (watchedTraktMovies == null)
                    {
                        UIUtils.UpdateStatus("Failed to get watched movies from trakt.tv, skipping watched movie import", true);
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        if (importCancelled) return;

                        UIUtils.UpdateStatus("Found {0} watched movies on trakt", watchedTraktMovies.Count());
                        UIUtils.UpdateStatus("Filtering out watched movies that are already watched on trakt.tv");

                        movies.RemoveAll(w => watchedTraktMovies.FirstOrDefault(t => t.Movie.Ids.ImdbId == w[IMDbFieldMapping.cIMDbID] || (t.Movie.Title.ToLowerInvariant() == w[IMDbFieldMapping.cTitle] && t.Movie.Year.ToString() == w[IMDbFieldMapping.cYear])) != null);

                        // mark all rated movies as watched
                        UIUtils.UpdateStatus("Importing {0} IMDb movies as watched...", movies.Count);

                        int pageSize = AppSettings.BatchSize;
                        int pages = (int)Math.Ceiling((double)movies.Count / pageSize);
                        for (int i = 0; i < pages; i++)
                        {
                            UIUtils.UpdateStatus("Importing page {0}/{1} IMDb movies as watched...", i + 1, pages);

                            var response = TraktAPI.AddMoviesToWatchedHistory(Helper.GetSyncWatchedMoviesData(movies.Skip(i * pageSize).Take(pageSize).ToList()));
                            if (response == null)
                            {
                                UIUtils.UpdateStatus("Failed to send watched status for IMDb movies", true);
                                Thread.Sleep(2000);
                            }
                            else if (response.NotFound.Movies.Count > 0)
                            {
                                UIUtils.UpdateStatus("Unable to sync watched state for {0} IMDb movies as they're not found on trakt.tv!", response.NotFound.Movies.Count);
                                Thread.Sleep(1000);
                            }

                            if (importCancelled) return;
                        }
                    }
                }
                #endregion

                #region Episodes

                if (imdbEpisodes != null && imdbEpisodes.Any())
                {
                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)imdbEpisodes.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        var episodesWatched = Helper.GetTraktEpisodeWatchedData(imdbEpisodes.Skip(i * pageSize).Take(pageSize));

                        UIUtils.UpdateStatus("Importing page {0}/{1} IMDb watched episodes...", i + 1, pages);

                        var response = TraktAPI.AddEpisodesToWatchedHistory(episodesWatched);
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Error importing IMDb episodes as watched to trakt.tv", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Episodes.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync {0} IMDb episodes as watched, as they're not found on trakt.tv!", response.NotFound.Episodes.Count);
                            Thread.Sleep(1000);
                        }

                        if (importCancelled) return;
                    }
                }
                #endregion
            }
            #endregion
       
            #region Sync Watchlist
            if (AppSettings.IMDbSyncWatchlist)
            {
                #region Movies
                watchlistMovies.AddRange(watchlistItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Movie));
                if (watchlistMovies.Any())
                {
                    UIUtils.UpdateStatus("Requesting existing watchlist movies from trakt...");
                    var watchlistTraktMovies = TraktAPI.GetWatchlistMovies();
                    if (watchlistTraktMovies != null)
                    {
                        UIUtils.UpdateStatus("Found {0} watchlist movies on trakt", watchlistTraktMovies.Count());
                        UIUtils.UpdateStatus("Filtering out watchlist movies that are already in watchlist on trakt.tv");
                        watchlistMovies.RemoveAll(w => watchlistTraktMovies.FirstOrDefault(t => t.Movie.Ids.ImdbId == w[IMDbFieldMapping.cIMDbID] || (t.Movie.Title.ToLowerInvariant() == w[IMDbFieldMapping.cTitle] && t.Movie.Year.ToString() == w[IMDbFieldMapping.cYear])) != null);
                    }

                    if (AppSettings.IgnoreWatchedForWatchlist && movies.Count > 0)
                    {
                        UIUtils.UpdateStatus("Requesting watched movies from trakt...");

                        // get watched movies from trakt so we don't import movies into watchlist that are already watched
                        if (watchedTraktMovies != null)
                        {
                            watchedTraktMovies = TraktAPI.GetWatchedMovies();
                            if (watchedTraktMovies == null)
                            {
                                UIUtils.UpdateStatus("Failed to get watched movies from trakt.tv", true);
                                Thread.Sleep(2000);
                            }
                            else
                            {
                                UIUtils.UpdateStatus("Found {0} watched movies on trakt", watchedTraktMovies.Count());
                                UIUtils.UpdateStatus("Filtering out watchlist movies that are watched on trakt.tv");

                                // remove movies from sync list which are watched already
                                watchlistMovies.RemoveAll(w => watchedTraktMovies.FirstOrDefault(t => t.Movie.Ids.ImdbId == w[IMDbFieldMapping.cIMDbID] || (t.Movie.Title.ToLowerInvariant() == w[IMDbFieldMapping.cTitle] && t.Movie.Year.ToString() == w[IMDbFieldMapping.cYear])) != null);
                            }
                        }
                    }

                    // add all movies to watchlist
                    UIUtils.UpdateStatus("Importing {0} IMDb watchlist movies to trakt.tv ...", watchlistMovies.Count());

                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)watchlistMovies.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        UIUtils.UpdateStatus("Importing page {0}/{1} IMDb movies into watchlist...", i + 1, pages);

                        var response = TraktAPI.AddMoviesToWatchlist(Helper.GetSyncMoviesData(watchlistMovies.Skip(i * pageSize).Take(pageSize).ToList()));
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Failed to send watchlist for IMDb movies.", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Movies.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync watchlist for {0} IMDb movies as they're not found on trakt.tv!", response.NotFound.Movies.Count);
                            Thread.Sleep(1000);
                        }

                        if (importCancelled) return;
                    }
                }
                #endregion

                #region TV Shows
                IEnumerable<TraktShowPlays> watchedTraktShows = null;
                watchlistShows.AddRange(watchlistItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Show));
                if (watchlistShows.Any())
                {
                    UIUtils.UpdateStatus("Requesting existing watchlist shows from trakt...");
                    var watchlistTraktShows = TraktAPI.GetWatchlistShows();
                    if (watchlistTraktShows != null)
                    {
                        UIUtils.UpdateStatus("Found {0} watchlist shows on trakt", watchlistTraktShows.Count());
                        UIUtils.UpdateStatus("Filtering out watchlist shows that are already in watchlist on trakt.tv");
                        watchlistShows.RemoveAll(w => watchlistTraktShows.FirstOrDefault(t => t.Show.Ids.ImdbId == w[IMDbFieldMapping.cIMDbID] || (t.Show.Title.ToLowerInvariant() == w[IMDbFieldMapping.cTitle].ToLowerInvariant() && t.Show.Year.ToString() == w[IMDbFieldMapping.cYear])) != null);
                    }

                    if (AppSettings.IgnoreWatchedForWatchlist && shows.Count > 0)
                    {
                        UIUtils.UpdateStatus("Requesting watched shows from trakt...");

                        // get watched movies from trakt so we don't import shows into watchlist that are already watched
                        watchedTraktShows = TraktAPI.GetWatchedShows();
                        if (watchedTraktShows != null)
                        {
                            UIUtils.UpdateStatus("Found {0} watched shows on trakt", watchedTraktShows.Count());
                            UIUtils.UpdateStatus("Filtering out watchlist shows containing watched episodes on trakt.tv");

                            // remove shows from sync list which are watched already
                            watchlistShows.RemoveAll(w => watchedTraktShows.Count(t => (t.Show.Ids.ImdbId == w[IMDbFieldMapping.cIMDbID]) || (t.Show.Title.ToLowerInvariant() == w[IMDbFieldMapping.cTitle].ToLowerInvariant() && t.Show.Year.ToString() == w[IMDbFieldMapping.cYear])) != 0);
                        }
                    }

                    //add all shows to watchlist
                    UIUtils.UpdateStatus("Importing {0} IMDb watchlist shows to trakt.tv...", watchlistShows.Count());

                    int pageSize = AppSettings.BatchSize;
                    int pages = (int)Math.Ceiling((double)watchlistShows.Count / pageSize);
                    for (int i = 0; i < pages; i++)
                    {
                        UIUtils.UpdateStatus("Importing page {0}/{1} IMDb shows into watchlist...", i + 1, pages);

                        var response = TraktAPI.AddShowsToWatchlist(Helper.GetSyncShowsData(watchlistShows.Skip(i * pageSize).Take(pageSize)));
                        if (response == null)
                        {
                            UIUtils.UpdateStatus("Failed to send watchlist for IMDb tv shows", true);
                            Thread.Sleep(2000);
                        }
                        else if (response.NotFound.Shows.Count > 0)
                        {
                            UIUtils.UpdateStatus("Unable to sync watchlist for {0} IMDb shows as they're not found on trakt.tv!", response.NotFound.Shows.Count);
                            Thread.Sleep(1000);
                        }

                        if (importCancelled) return;
                    }
                }
                #endregion

                #region Episodes
                imdbEpisodes.Clear();
                episodes = watchlistItems.Where(r => r[IMDbFieldMapping.cType].ItemType() == IMDbType.Episode).ToList();
                if (episodes.Any())
                {
                    UIUtils.UpdateStatus("Found {0} IMDb watchlist episodes", episodes.Count());

                    imdbEpisodes.AddRange(episodes.Select(Helper.GetIMDbEpisodeFromTrakt).Where(imdbEpisode => imdbEpisode != null));

                    // filter out existing watchlist episodes
                    UIUtils.UpdateStatus("Requesting existing watchlist episodes from trakt...");
                    var watchlistTraktEpisodes = TraktAPI.GetWatchlistEpisodes();
                    if (watchlistTraktEpisodes != null)
                    {
                        UIUtils.UpdateStatus("Found {0} watchlist episodes on trakt", watchlistTraktEpisodes.Count());
                        UIUtils.UpdateStatus("Filtering out watchlist episodes that are already in watchlist on trakt.tv");
                        imdbEpisodes.RemoveAll(e => watchlistTraktEpisodes.FirstOrDefault(w => w.Episode.Ids.Trakt == e.TraktId) != null);
                    }

                    if (AppSettings.IgnoreWatchedForWatchlist && episodes.Count > 0)
                    {
                        // we already might have it from the shows sync
                        if (watchedTraktShows == null)
                        {
                            UIUtils.UpdateStatus("Requesting watched episodes from trakt...");

                            // get watched episodes from trakt so we don't import episodes into watchlist that are already watched
                            watchedTraktShows = TraktAPI.GetWatchedShows();
                        }

                        if (watchedTraktShows != null)
                        {
                            UIUtils.UpdateStatus("Filtering out watchlist episodes containing watched episodes on trakt.tv.");

                            imdbEpisodes.RemoveAll(e => watchedTraktShows.Where(s => s.Show.Ids.ImdbId == e.ImdbId)
                                                                         .Any(s => s.Seasons.Exists(se => se.Number == e.SeasonNumber && se.Episodes.Exists(ep => ep.Number == e.EpisodeNumber))));
                        }
                    }

                    UIUtils.UpdateStatus("Importing {0} episodes in watchlist to trakt.tv", imdbEpisodes.Count());

                    if (imdbEpisodes.Count > 0)
                    {
                        int pageSize = AppSettings.BatchSize;
                        int pages = (int)Math.Ceiling((double)imdbEpisodes.Count / pageSize);
                        for (int i = 0; i < pages; i++)
                        {
                            UIUtils.UpdateStatus(string.Format("Importing page {0}/{1} IMDb watchlist episodes...", i + 1, pages));

                            var response = TraktAPI.AddEpisodesToWatchlist(Helper.GetTraktEpisodeData(imdbEpisodes.Skip(i * pageSize).Take(pageSize)));
                            if (response == null)
                            {
                                UIUtils.UpdateStatus("Error importing IMDb episode watchlist to trakt.tv", true);
                                Thread.Sleep(2000);
                            }
                            else if (response.NotFound.Episodes.Count > 0)
                            {
                                UIUtils.UpdateStatus("Unable to sync watchlist for {0} IMDb episodes as they're not found on trakt.tv!", response.NotFound.Episodes.Count);
                                Thread.Sleep(1000);
                            }

                            if (importCancelled) return;
                        }
                    }
                }
                #endregion
            }
            #endregion
        }

        public void Cancel()
        {
            // signals to cancel import
            importCancelled = true;
        }

        #endregion
    }
}