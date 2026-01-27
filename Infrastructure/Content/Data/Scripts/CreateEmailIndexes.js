// MongoDB Script to Create Case-Insensitive Unique Indexes on Email Fields
// Purpose: Prevent duplicate user registrations with different email capitalizations
// Run this script using: mongosh <database-name> CreateEmailIndexes.js

// Use the appropriate database
// db = db.getSiblingDB('CarePro'); // Uncomment and set your database name

print('Creating case-insensitive unique indexes for email fields...\n');

// 1. Create unique index on CareGivers collection
print('1. Creating index on CareGivers.Email...');
try {
    db.CareGivers.createIndex(
        { Email: 1 },
        { 
            unique: true,
            collation: { locale: 'en', strength: 2 },
            name: 'email_unique_case_insensitive'
        }
    );
    print('   ✓ Successfully created unique index on CareGivers.Email\n');
} catch (error) {
    print('   ✗ Error creating index on CareGivers.Email: ' + error.message);
    print('   Note: If index already exists or there are duplicate emails, this will fail.\n');
}

// 2. Create unique index on Clients collection
print('2. Creating index on Clients.Email...');
try {
    db.Clients.createIndex(
        { Email: 1 },
        { 
            unique: true,
            collation: { locale: 'en', strength: 2 },
            name: 'email_unique_case_insensitive'
        }
    );
    print('   ✓ Successfully created unique index on Clients.Email\n');
} catch (error) {
    print('   ✗ Error creating index on Clients.Email: ' + error.message);
    print('   Note: If index already exists or there are duplicate emails, this will fail.\n');
}

// 3. Create unique index on AppUsers collection
print('3. Creating index on AppUsers.Email...');
try {
    db.AppUsers.createIndex(
        { Email: 1 },
        { 
            unique: true,
            collation: { locale: 'en', strength: 2 },
            name: 'email_unique_case_insensitive'
        }
    );
    print('   ✓ Successfully created unique index on AppUsers.Email\n');
} catch (error) {
    print('   ✗ Error creating index on AppUsers.Email: ' + error.message);
    print('   Note: If index already exists or there are duplicate emails, this will fail.\n');
}

// Verify indexes were created
print('\n=== Verification ===\n');

print('CareGivers indexes:');
printjson(db.CareGivers.getIndexes());

print('\nClients indexes:');
printjson(db.Clients.getIndexes());

print('\nAppUsers indexes:');
printjson(db.AppUsers.getIndexes());

print('\n=== Index Creation Complete ===');
print('\nNOTE: If you encounter duplicate key errors, you need to:');
print('1. Identify duplicate emails using the provided query scripts');
print('2. Remove or merge duplicate records');
print('3. Re-run this script to create the unique indexes');
