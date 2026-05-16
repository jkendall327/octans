# Terminology of note

*File*: bytes on a disk, the operating-system notion of a file.

*Image*: a file that's a JPEG, PNG etc.

*Media*: any type of file that can be imported in Octans. 
Includes images but also (potentially) videos, music, archives.

*Hash*: the hash of the bytes of a file. 
Used by Octans to determine where imported media is stored physically. 
`HashItem` is the primary instantiation.

*Importing*: introducing media to Octan's purview. 
Can entail copying files from the local system, downloading stuff over HTTP, etc.

*Download*: normal HTTP downloads. One method of importing.

*Repository* a demarcationary universe that a piece of media lives within. 
Repositories include:
- Inbox. Newly-imported media.
- Trash. Deleted media. The corresponding physical file may be deleted without warning.
- Archive. Media the user has elected to keep permanently.

Users can create arbitrary repositories.

*Downloader*: user-created construct containing Lua scripts for interfacing with websites.

*Subscription*: user-specified query that scans a given web source for new content on a periodic timer.