#!/bin/bash
# MongoDB initialization script for CarePro

# Switch to the CarePro database
mongosh <<EOF
use carepro

// Create application user with read/write access
db.createUser({
  user: "carepro_user",
  pwd: "carepro_password",
  roles: [
    {
      role: "readWrite",
      db: "carepro"
    }
  ]
})

// Create collections with validation schemas
db.createCollection("contracts", {
  validator: {
    \$jsonSchema: {
      bsonType: "object",
      required: ["id", "title", "payerId", "caregiverId", "amount", "status", "createdAt"],
      properties: {
        id: {
          bsonType: "string",
          description: "Contract ID must be a string and is required"
        },
        title: {
          bsonType: "string",
          description: "Title must be a string and is required"
        },
        description: {
          bsonType: "string",
          description: "Description must be a string"
        },
        payerId: {
          bsonType: "string",
          description: "Payer ID must be a string and is required"
        },
        caregiverId: {
          bsonType: "string",
          description: "Caregiver ID must be a string and is required"
        },
        amount: {
          bsonType: "decimal",
          description: "Amount must be a decimal and is required"
        },
        status: {
          bsonType: "string",
          enum: ["Pending", "Active", "Completed", "Cancelled"],
          description: "Status must be one of the enum values and is required"
        },
        createdAt: {
          bsonType: "date",
          description: "Created date must be a date and is required"
        }
      }
    }
  }
})

db.createCollection("locations", {
  validator: {
    \$jsonSchema: {
      bsonType: "object",
      required: ["id", "address", "latitude", "longitude", "contractId"],
      properties: {
        id: {
          bsonType: "string",
          description: "Location ID must be a string and is required"
        },
        address: {
          bsonType: "string",
          description: "Address must be a string and is required"
        },
        latitude: {
          bsonType: "double",
          description: "Latitude must be a double and is required"
        },
        longitude: {
          bsonType: "double",
          description: "Longitude must be a double and is required"
        },
        contractId: {
          bsonType: "string",
          description: "Contract ID must be a string and is required"
        }
      }
    }
  }
})

// Create indexes for better performance
db.contracts.createIndex({ "payerId": 1 })
db.contracts.createIndex({ "caregiverId": 1 })
db.contracts.createIndex({ "status": 1 })
db.contracts.createIndex({ "createdAt": -1 })

db.locations.createIndex({ "contractId": 1 })
db.locations.createIndex({ "latitude": 1, "longitude": 1 })

print("CarePro database initialized successfully!")
EOF