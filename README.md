# OldSchoolResearch

Research with a chance of success instead of certainty, either with scrap or another item set by the admin.

This replaces the functionality of a deployed research table with it's own button to begin research, etc.

A player will insert the item and the currency item, whether scrap or ducttape, etc.  If, for instance, the normal research cost of an item is 75 and they only have 50, the chance will be roughly 66%.  If they have 75, it should research normally.

In place of the research cost will be a research cost and chance notice.

The sounds are the same so it won't be a huge shock.  However, since they moved to using scrap, the GUI would not let you research unless you had 100% of the requirement.  So, it's probably been awhile since you heard the breakage sound on failure.

## Configuration
```json
{
  "debug": false,
  "currency": "ducttape",
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 3
  }
}
```

