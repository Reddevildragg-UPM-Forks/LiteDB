﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB
{
    internal class PageService
    {
        private IDiskService _disk;
        private Logger _log;

        private SortedDictionary<uint, BasePage> _cache = new SortedDictionary<uint, BasePage>();

        public PageService(IDiskService disk, Logger log)
        {
            _disk = disk;
            _log = log;
        }

        /// <summary>
        /// Get a page from cache or from disk (and put on cache)
        /// </summary>
        public T GetPage<T>(uint pageID, bool markAsDirty = false)
            where T : BasePage
        {
            // lock concurrency access (read access are not in a lock transaction)
            lock(_cache)
            {
                var page = _cache.GetOrDefault(pageID);

                // is not on cache? load from disk
                if (page == null)
                {
                    var buffer = _disk.ReadPage(pageID);
                    page = BasePage.ReadPage(buffer);
                    _cache.Add(pageID, page);
                }

                if (markAsDirty)
                {
                    page.IsDirty = true;
                }

                return (T)page;
            }
        }

        /// <summary>
        /// Return all dirty pages
        /// </summary>
        public IEnumerable<BasePage> GetDirtyPages()
        {
            return _cache.Values.Where(x => x.IsDirty);
        }

        /// <summary>
        /// Clear all pages from cache memory
        /// </summary>
        public void ClearCache()
        {
            _log.Write(Logger.CACHE, "clearing cache");

            // cache must be cleared all pages (pages reference problem when dirty pages only)
            _cache.Clear();
        }

        /// <summary>
        /// Get how many pages are in cache
        /// </summary>
        public int PagesInCache { get { return _cache.Count; } }

        /// <summary>
        /// Read all sequences pages from a start pageID (using NextPageID)
        /// </summary>
        public IEnumerable<T> GetSeqPages<T>(uint firstPageID)
            where T : BasePage
        {
            var pageID = firstPageID;

            while (pageID != uint.MaxValue)
            {
                var page = this.GetPage<T>(pageID);

                pageID = page.NextPageID;

                yield return page;
            }
        }

        /// <summary>
        /// Get a new empty page - can be a reused page (EmptyPage) or a clean one (extend datafile)
        /// [Set as Dirty]
        /// </summary>
        public T NewPage<T>(BasePage prevPage = null)
            where T : BasePage
        {
            // get header
            var header = this.GetPage<HeaderPage>(0, true);
            var pageID = (uint)0;
            var diskData = new byte[0];

            // try get page from Empty free list
            if (header.FreeEmptyPageID != uint.MaxValue)
            {
                var free = this.GetPage<BasePage>(header.FreeEmptyPageID);

                // remove page from empty list
                this.AddOrRemoveToFreeList(false, free, header, ref header.FreeEmptyPageID);

                pageID = free.PageID;

                // if used page has original disk data, copy to my new page
                if (free.DiskData.Length > 0)
                {
                    diskData = new byte[BasePage.PAGE_SIZE];
                    Buffer.BlockCopy(diskData, 0, diskData, 0, BasePage.PAGE_SIZE);
                }
            }
            else
            {
                pageID = ++header.LastPageID;
            }

            var page = BasePage.CreateInstance<T>(pageID);

            // copy disk data from re-used page (or be an empty)
            page.DiskData = diskData;

            // add page to cache with correct T type (could be an old Empty page type)
            _cache[pageID] = page;

            // mark new page as dirty
            page.IsDirty = true;

            // if there a page before, just fix NextPageID pointer
            if (prevPage != null)
            {
                prevPage.IsDirty = true;
                page.PrevPageID = prevPage.PageID;
                prevPage.NextPageID = page.PageID;
            }

            return page;
        }

        /// <summary>
        /// Delete an page using pageID - transform them in Empty Page and add to EmptyPageList
        /// If you delete a page, you can using same old instance of page - page will be converter to EmptyPage
        /// If need deleted page, use GetPage again
        /// [Set as Dirty]
        /// </summary>
        public void DeletePage(uint pageID, bool addSequence = false)
        {
            // get all pages in sequence or a single one
            var pages = addSequence ? this.GetSeqPages<BasePage>(pageID).ToArray() : new BasePage[] { this.GetPage<BasePage>(pageID) };

            // get my header page
            var header = this.GetPage<HeaderPage>(0);

            // adding all pages to FreeList
            foreach (var page in pages)
            {
                // mark old content page as dirty to write on journal if needed
                page.IsDirty = true;

                // create a new empty page based on a normal page
                var empty = new EmptyPage(page.PageID);

                // empty page must be marked as dirty to be saved when commit
                empty.IsDirty = true;
                
                // update cache with empty page marked as dirty
                _cache[page.PageID] = empty;

                // add to empty free list
                this.AddOrRemoveToFreeList(true, empty, header, ref header.FreeEmptyPageID);
            }
        }

        /// <summary>
        /// Returns a page that contains space enouth to data to insert new object - if not exits, create a new Page
        /// [Set as Dirty]
        /// </summary>
        public T GetFreePage<T>(uint startPageID, int size)
            where T : BasePage
        {
            if (startPageID != uint.MaxValue)
            {
                // get the first page
                //var page = this.GetPage<BasePage>(startPageID);
                var page = this.GetPage<T>(startPageID);

                // check if there space in this page
                var free = page.FreeBytes;

                // first, test if there is space on this page
                if (free >= size)
                {
                    page.IsDirty = true;

                    return page;
                }
            }

            // if not has space on first page, there is no page with space (pages are ordered), create a new one
            return this.NewPage<T>();
        }

        #region Add Or Remove do empty list

        /// <summary>
        /// Add or Remove a page in a sequence
        /// [CAN set "page" and "startPage" as Dirty]
        /// </summary>
        /// <param name="add">Indicate that will add or remove from FreeList</param>
        /// <param name="page">Page to add or remove from FreeList</param>
        /// <param name="startPage">Page reference where start the header list node</param>
        /// <param name="fieldPageID">Field reference, from startPage</param>
        public void AddOrRemoveToFreeList(bool add, BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            if (add)
            {
                // if page has no prev/next it's not on list - lets add
                if (page.PrevPageID == uint.MaxValue && page.NextPageID == uint.MaxValue)
                {
                    this.AddToFreeList(page, startPage, ref fieldPageID);
                }
                else
                {
                    // othersie this page is already in this list, lets move do put in free size desc order
                    this.MoveToFreeList(page, startPage, ref fieldPageID);
                }
            }
            else
            {
                // if this page is not in sequence, its not on freelist
                if (page.PrevPageID == uint.MaxValue && page.NextPageID == uint.MaxValue)
                    return;

                this.RemoveToFreeList(page, startPage, ref fieldPageID);
            }
        }

        /// <summary>
        /// Add a page in free list in desc free size order
        /// </summary>
        private void AddToFreeList(BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            var free = page.FreeBytes;
            var nextPageID = fieldPageID;
            BasePage next = null;

            // base must be marked as dirty
            page.IsDirty = true;

            // let's page in desc order
            while (nextPageID != uint.MaxValue)
            {
                next = this.GetPage<BasePage>(nextPageID);

                if (free >= next.FreeBytes)
                {
                    // mark next page as dirty
                    next.IsDirty = true;

                    // assume my page in place of next page
                    page.PrevPageID = next.PrevPageID;
                    page.NextPageID = next.PageID;

                    // link next page to my page
                    next.PrevPageID = page.PageID;

                    // my page is the new first page on list
                    if (page.PrevPageID == 0)
                    {
                        startPage.IsDirty = true;
                        fieldPageID = page.PageID;
                    }
                    else
                    {
                        // if not the first, ajust links from previous page (set as dirty)
                        var prev = this.GetPage<BasePage>(page.PrevPageID);
                        prev.IsDirty = true;
                        prev.NextPageID = page.PageID;
                    }

                    return; // job done - exit
                }

                nextPageID = next.NextPageID;
            }

            // empty list, be the first
            if (next == null)
            {
                startPage.IsDirty = true;

                // it's first page on list
                page.PrevPageID = 0;
                fieldPageID = page.PageID;
            }
            else
            {
                // it's last position on list (next = last page on list)
                page.PrevPageID = next.PageID;
                next.NextPageID = page.PageID;
                next.IsDirty = true;
            }
        }

        /// <summary>
        /// Remove a page from list - the ease part
        /// </summary>
        private void RemoveToFreeList(BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            // mark page that will be removed as dirty
            page.IsDirty = true;

            // this page is the first of list
            if (page.PrevPageID == 0)
            {
                startPage.IsDirty = true;
                fieldPageID = page.NextPageID;
            }
            else
            {
                // if not the first, get previous page to remove NextPageId (set as dirty)
                var prevPage = this.GetPage<BasePage>(page.PrevPageID, true);
                prevPage.NextPageID = page.NextPageID;
            }

            // if my page is not the last on sequence, ajust the last page (set as dirty)
            if (page.NextPageID != uint.MaxValue)
            {
                var nextPage = this.GetPage<BasePage>(page.NextPageID, true);
                nextPage.PrevPageID = page.PrevPageID;
            }

            page.PrevPageID = page.NextPageID = uint.MaxValue;
        }

        /// <summary>
        /// When a page is already on a list it's more efficient just move comparing with sinblings
        /// </summary>
        private void MoveToFreeList(BasePage page, BasePage startPage, ref uint fieldPageID)
        {
            //TODO: write a better solution
            this.RemoveToFreeList(page, startPage, ref fieldPageID);
            this.AddToFreeList(page, startPage, ref fieldPageID);
        }

        #endregion
    }
}