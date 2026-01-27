// MongoDB Script to Find Duplicate Emails (Case-Insensitive)
// Purpose: Identify duplicate email addresses before creating unique indexes
// Run this script using: mongosh <database-name> FindDuplicateEmails.js

print('Searching for duplicate emails across all collections...\n');

// 1. Find duplicates in CareGivers collection
print('=== CareGivers Collection ===');
const caregiverDuplicates = db.CareGivers.aggregate([
    {
        $group: {
            _id: { $toLower: "$Email" },
            originalEmails: { $addToSet: "$Email" },
            count: { $sum: 1 },
            ids: { $push: "$_id" },
            createdDates: { $push: "$CreatedAt" }
        }
    },
    {
        $match: {
            count: { $gt: 1 }
        }
    },
    {
        $sort: { count: -1 }
    }
]).toArray();

if (caregiverDuplicates.length > 0) {
    print('Found ' + caregiverDuplicates.length + ' duplicate email(s) in CareGivers:');
    caregiverDuplicates.forEach(function(dup) {
        print('  Email: ' + dup._id + ' (Count: ' + dup.count + ')');
        print('  Variations: ' + dup.originalEmails.join(', '));
        print('  IDs: ' + dup.ids.join(', '));
        print('');
    });
} else {
    print('✓ No duplicate emails found in CareGivers\n');
}

// 2. Find duplicates in Clients collection
print('=== Clients Collection ===');
const clientDuplicates = db.Clients.aggregate([
    {
        $group: {
            _id: { $toLower: "$Email" },
            originalEmails: { $addToSet: "$Email" },
            count: { $sum: 1 },
            ids: { $push: "$_id" },
            createdDates: { $push: "$CreatedAt" }
        }
    },
    {
        $match: {
            count: { $gt: 1 }
        }
    },
    {
        $sort: { count: -1 }
    }
]).toArray();

if (clientDuplicates.length > 0) {
    print('Found ' + clientDuplicates.length + ' duplicate email(s) in Clients:');
    clientDuplicates.forEach(function(dup) {
        print('  Email: ' + dup._id + ' (Count: ' + dup.count + ')');
        print('  Variations: ' + dup.originalEmails.join(', '));
        print('  IDs: ' + dup.ids.join(', '));
        print('');
    });
} else {
    print('✓ No duplicate emails found in Clients\n');
}

// 3. Find duplicates in AppUsers collection
print('=== AppUsers Collection ===');
const appUserDuplicates = db.AppUsers.aggregate([
    {
        $group: {
            _id: { $toLower: "$Email" },
            originalEmails: { $addToSet: "$Email" },
            count: { $sum: 1 },
            ids: { $push: "$_id" },
            roles: { $push: "$Role" },
            createdDates: { $push: "$CreatedAt" }
        }
    },
    {
        $match: {
            count: { $gt: 1 }
        }
    },
    {
        $sort: { count: -1 }
    }
]).toArray();

if (appUserDuplicates.length > 0) {
    print('Found ' + appUserDuplicates.length + ' duplicate email(s) in AppUsers:');
    appUserDuplicates.forEach(function(dup) {
        print('  Email: ' + dup._id + ' (Count: ' + dup.count + ')');
        print('  Variations: ' + dup.originalEmails.join(', '));
        print('  Roles: ' + dup.roles.join(', '));
        print('  IDs: ' + dup.ids.join(', '));
        print('');
    });
} else {
    print('✓ No duplicate emails found in AppUsers\n');
}

// 4. Find cross-collection duplicates (same email in different user types)
print('=== Cross-Collection Email Check ===');
print('Checking for emails that exist in multiple user type collections...\n');

const allCaregiverEmails = db.CareGivers.find({}, { Email: 1, _id: 0 }).toArray().map(doc => doc.Email.toLowerCase());
const allClientEmails = db.Clients.find({}, { Email: 1, _id: 0 }).toArray().map(doc => doc.Email.toLowerCase());

const crossDuplicates = allCaregiverEmails.filter(email => allClientEmails.includes(email));

if (crossDuplicates.length > 0) {
    print('Found ' + crossDuplicates.length + ' email(s) registered as BOTH Caregiver AND Client:');
    crossDuplicates.forEach(function(email) {
        print('  - ' + email);
    });
    print('');
} else {
    print('✓ No emails found registered in both CareGivers and Clients\n');
}

// Summary
print('=== Summary ===');
print('CareGivers duplicates: ' + caregiverDuplicates.length);
print('Clients duplicates: ' + clientDuplicates.length);
print('AppUsers duplicates: ' + appUserDuplicates.length);
print('Cross-collection duplicates: ' + crossDuplicates.length);
print('\nIf any duplicates were found, resolve them before creating unique indexes.');
