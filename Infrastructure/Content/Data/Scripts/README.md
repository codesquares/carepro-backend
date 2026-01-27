# MongoDB Email Index Management Scripts

This directory contains scripts to manage email uniqueness constraints in the CarePro MongoDB database.

## Scripts Overview

### 1. FindDuplicateEmails.js
**Purpose**: Identifies duplicate email addresses across all user collections before creating unique indexes.

**Usage**:
```bash
mongosh <your-database-name> FindDuplicateEmails.js
```

**What it checks**:
- Duplicate emails within CareGivers collection (case-insensitive)
- Duplicate emails within Clients collection (case-insensitive)
- Duplicate emails within AppUsers collection (case-insensitive)
- Cross-collection duplicates (same email as both Caregiver and Client)

**When to run**: Before creating unique indexes or when troubleshooting registration issues.

---

### 2. CreateEmailIndexes.js
**Purpose**: Creates case-insensitive unique indexes on Email fields to prevent duplicate registrations.

**Usage**:
```bash
mongosh <your-database-name> CreateEmailIndexes.js
```

**What it creates**:
- Unique index on `CareGivers.Email` with case-insensitive collation
- Unique index on `Clients.Email` with case-insensitive collation
- Unique index on `AppUsers.Email` with case-insensitive collation

**Index Details**:
- Name: `email_unique_case_insensitive`
- Collation: `{ locale: 'en', strength: 2 }` (case-insensitive, accent-insensitive)
- Unique: `true`

**When to run**: 
- After resolving any duplicate emails found by FindDuplicateEmails.js
- During initial deployment
- After database restoration

---

## Recommended Workflow

### Initial Setup (New Database)
```bash
# 1. Check for any existing duplicates
mongosh your_database_name FindDuplicateEmails.js

# 2. If no duplicates, create indexes
mongosh your_database_name CreateEmailIndexes.js
```

### Production Deployment (Existing Database)
```bash
# 1. Find duplicates first
mongosh production_db FindDuplicateEmails.js > duplicates_report.txt

# 2. Review and resolve duplicates manually
# (See "Handling Duplicates" section below)

# 3. After resolving duplicates, create indexes
mongosh production_db CreateEmailIndexes.js

# 4. Verify indexes were created
mongosh production_db --eval "db.CareGivers.getIndexes()"
mongosh production_db --eval "db.Clients.getIndexes()"
mongosh production_db --eval "db.AppUsers.getIndexes()"
```

---

## Handling Duplicates

If FindDuplicateEmails.js identifies duplicates, you need to resolve them before creating indexes.

### Strategy 1: Keep Oldest Account
```javascript
// Example: Keep the account with earliest CreatedAt date
// Run in mongosh for each duplicate email found

const duplicateEmail = "user@example.com"; // Replace with actual email

// Find all records with this email (case-insensitive)
const records = db.CareGivers.find({ Email: { $regex: new RegExp("^" + duplicateEmail + "$", "i") } }).toArray();

// Sort by CreatedAt and keep the first one
records.sort((a, b) => new Date(a.CreatedAt) - new Date(b.CreatedAt));
const keepId = records[0]._id;

// Delete the others
records.slice(1).forEach(record => {
    db.CareGivers.deleteOne({ _id: record._id });
    print("Deleted duplicate: " + record._id);
});
```

### Strategy 2: Soft Delete Duplicates
```javascript
// Mark duplicates as deleted instead of removing them
const duplicateEmail = "user@example.com";

const records = db.CareGivers.find({ Email: { $regex: new RegExp("^" + duplicateEmail + "$", "i") } }).toArray();
records.sort((a, b) => new Date(a.CreatedAt) - new Date(b.CreatedAt));

// Keep first, soft delete others
records.slice(1).forEach(record => {
    db.CareGivers.updateOne(
        { _id: record._id },
        { 
            $set: { 
                IsDeleted: true, 
                DeletedOn: new Date(),
                Email: record.Email + "_deleted_" + record._id
            }
        }
    );
    print("Soft deleted: " + record._id);
});
```

---

## Verifying Index Status

### Check if indexes exist:
```bash
mongosh your_database_name --eval "db.CareGivers.getIndexes()"
mongosh your_database_name --eval "db.Clients.getIndexes()"
mongosh your_database_name --eval "db.AppUsers.getIndexes()"
```

### Check index is working (should fail):
```javascript
// Try to insert duplicate - should fail with duplicate key error
db.CareGivers.insertOne({
    Email: "test@example.com",
    FirstName: "Test",
    LastName: "User"
});

// Try again with different case - should also fail
db.CareGivers.insertOne({
    Email: "Test@Example.com",
    FirstName: "Test2",
    LastName: "User2"
});
```

---

## Removing Indexes (If Needed)

If you need to remove the indexes (e.g., for troubleshooting):

```bash
mongosh your_database_name
```

```javascript
db.CareGivers.dropIndex("email_unique_case_insensitive");
db.Clients.dropIndex("email_unique_case_insensitive");
db.AppUsers.dropIndex("email_unique_case_insensitive");
```

---

## Troubleshooting

### Error: "E11000 duplicate key error"
**Cause**: Trying to create unique index when duplicates exist in the database.

**Solution**: Run FindDuplicateEmails.js, resolve duplicates, then retry CreateEmailIndexes.js.

### Error: "Index already exists"
**Cause**: Index was previously created.

**Solution**: No action needed, or drop and recreate if you need to change index properties.

### Performance Issues
**Note**: Creating indexes on large collections may take time. Consider running during off-peak hours.

---

## Integration with Application Code

These indexes work in conjunction with the application-level validation in:
- `Infrastructure/Content/Services/CareGiverService.cs`
- `Infrastructure/Content/Services/ClientService.cs`
- `Infrastructure/Content/Data/CareProDbContext.cs`

The application code performs checks before insertion, and the database indexes provide a safety net for race conditions and data integrity.

---

## Maintenance

**Recommended Schedule**:
- Run FindDuplicateEmails.js monthly to ensure data integrity
- Review logs for duplicate registration attempts
- Monitor for unusual patterns in email registration

---

## Contact

For questions or issues with these scripts, refer to the main project documentation or contact the development team.
